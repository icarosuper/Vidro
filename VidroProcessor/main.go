package main

import (
	"context"
	"errors"
	"fmt"
	"io"
	"net/http"
	"os"
	"os/signal"
	"path/filepath"
	"runtime"
	"sync"
	"syscall"
	"time"

	"github.com/prometheus/client_golang/prometheus/promhttp"
	"github.com/rs/zerolog"
	"github.com/rs/zerolog/log"
	"go.opentelemetry.io/otel/attribute"
	oteltrace "go.opentelemetry.io/otel/trace"

	"video-processor/config"
	"video-processor/internal/processor"
	processor_steps "video-processor/internal/processor/processor-steps"
	"video-processor/internal/telemetry"
	"video-processor/internal/webhook"
	"video-processor/metrics"
	"video-processor/minio"
	"video-processor/queue"
)

// bookkeepingTimeout caps the Redis writes that close a job out. It is short on purpose:
// these run after cancellation, inside the 30s graceful-shutdown window (design-decisions #12).
const bookkeepingTimeout = 10 * time.Second

// jobTimeout returns the whole-job budget: JOB_TIMEOUT when set, otherwise derived
// from the step timeouts so the budget can never be smaller than the pipeline.
func jobTimeout(cfg *config.Config) time.Duration {
	if cfg.JobTimeout > 0 {
		return cfg.JobTimeout
	}
	return processor.JobBudget(cfg.ProcessingTimeoutScale)
}

// workerCount returns how many queue-consuming goroutines to start: WORKER_COUNT when
// set, otherwise derived from the cores and from how many FFmpeg processes one job can
// spawn at once — see processor.DefaultWorkerCount.
func workerCount(cfg *config.Config) int {
	if cfg.WorkerCount > 0 {
		return cfg.WorkerCount
	}
	return processor.DefaultWorkerCount(
		runtime.NumCPU(), cfg.ParallelNonCriticalSteps, cfg.MaxParallelPostTranscodeSteps)
}

// logWriter returns the human-readable console writer when stderr is a terminal, and
// raw JSON otherwise. Under Docker/Promtail the output has to stay JSON so Loki can
// filter by field (videoID, workerID) instead of substring.
func logWriter() io.Writer {
	info, err := os.Stderr.Stat()
	isTerminal := err == nil && info.Mode()&os.ModeCharDevice != 0
	if isTerminal {
		return zerolog.ConsoleWriter{Out: os.Stderr}
	}
	return os.Stderr
}

func main() {
	// Configure zerolog
	zerolog.TimeFieldFormat = zerolog.TimeFormatUnix
	log.Logger = log.Output(logWriter())
	// Pipeline code logs through zerolog.Ctx(ctx); without this default it would be
	// silently disabled whenever no per-job logger was injected into the context.
	zerolog.DefaultContextLogger = &log.Logger

	cfg := config.LoadConfig()

	probeCtx, probeCancel := context.WithTimeout(context.Background(), 15*time.Second)
	videoEncoder := processor_steps.ResolveVideoEncoder(probeCtx, cfg.VideoEncoder)
	probeCancel()

	// Initialize tracing (no-op if OTEL_ENDPOINT is not configured)
	shutdownTracing, err := telemetry.Init(context.Background(), cfg.OTelServiceName, cfg.OTelEndpoint)
	if err != nil {
		log.Fatal().Err(err).Msg("Failed to initialize tracing")
	}
	defer func() {
		if err := shutdownTracing(context.Background()); err != nil {
			log.Warn().Err(err).Msg("Failed to shut down tracing")
		}
	}()

	initClients(cfg)

	// Start HTTP server with metrics and health check
	startHTTPServer(cfg.HTTPPort)

	numWorkers := workerCount(cfg)

	log.Info().
		Int("workers", numWorkers).
		Int("cores", runtime.NumCPU()).
		Bool("parallelNonCriticalSteps", cfg.ParallelNonCriticalSteps).
		Int("maxParallelPostTranscodeSteps", cfg.MaxParallelPostTranscodeSteps).
		Dur("jobTimeout", jobTimeout(cfg)).
		Msg("Starting video-processor")

	ctx, cancel := context.WithCancel(context.Background())

	// Goroutine that re-queues orphan jobs (crash during processing). The threshold
	// must stay above the job budget, otherwise a healthy long job gets requeued
	// while it is still running.
	go queue.StartRecovery(ctx, jobTimeout(cfg)+time.Minute)

	// Goroutine that updates the queue size metric every 30 seconds
	go func() {
		ticker := time.NewTicker(30 * time.Second)
		defer ticker.Stop()
		for {
			select {
			case <-ctx.Done():
				return
			case <-ticker.C:
				if size, err := queue.GetQueueSize(ctx); err == nil {
					metrics.QueueSize.Set(float64(size))
				}
			}
		}
	}()
	defer cancel()

	sigChan := make(chan os.Signal, 1)
	signal.Notify(sigChan, syscall.SIGINT, syscall.SIGTERM)

	var wg sync.WaitGroup

	// Start workers
	for i := range numWorkers {
		wg.Add(1)
		go func(workerID int) {
			defer wg.Done()
			for {
				select {
				case <-ctx.Done():
					log.Info().Int("workerID", workerID).Msg("Shutting down worker gracefully")
					return
				default:
					if err := processNextMessage(ctx, workerID, cfg, videoEncoder); err != nil {
						if !errors.Is(err, context.Canceled) {
							log.Error().Err(err).Int("workerID", workerID).Msg("Error processing message")
						}
					}
				}
			}
		}(i + 1)
	}

	// Wait for interrupt signal
	<-sigChan
	log.Warn().Msg("Shutdown signal received. Starting graceful shutdown")

	// Cancel context to initiate shutdown
	cancel()

	// Wait for workers with timeout
	shutdownComplete := make(chan struct{})
	go func() {
		wg.Wait()
		close(shutdownComplete)
	}()

	// Set a timeout for shutdown (30 seconds)
	select {
	case <-shutdownComplete:
		log.Info().Msg("All workers shut down normally")
	case <-time.After(30 * time.Second):
		log.Warn().Msg("Timeout reached. Forcing remaining workers to stop")
	}

	log.Info().Msg("Program terminated")
}

func initClients(cfg *config.Config) {
	queue.InitRedisClient(cfg)
	minio.InitMinioClient(cfg)
}

func startHTTPServer(port string) {
	http.HandleFunc("/health", healthCheckHandler)
	http.Handle("/metrics", promhttp.Handler())

	addr := ":" + port
	go func() {
		log.Info().Str("address", addr).Msg("HTTP server started")
		if err := http.ListenAndServe(addr, nil); err != nil {
			log.Fatal().Err(err).Msg("Failed to start HTTP server")
		}
	}()
}

func healthCheckHandler(w http.ResponseWriter, r *http.Request) {
	// Check Redis
	if err := queue.HealthCheck(r.Context()); err != nil {
		log.Error().Err(err).Msg("Redis health check failed")
		http.Error(w, "Redis unavailable", http.StatusServiceUnavailable)
		return
	}

	// Check MinIO
	if err := minio.HealthCheck(r.Context()); err != nil {
		log.Error().Err(err).Msg("MinIO health check failed")
		http.Error(w, "MinIO unavailable", http.StatusServiceUnavailable)
		return
	}

	w.WriteHeader(http.StatusOK)
	_, _ = w.Write([]byte("OK"))
}

func processNextMessage(ctx context.Context, workerID int, cfg *config.Config, videoEncoder string) error {
	// Blocks until a message is received or ctx is canceled (shutdown).
	// BRPOPLPUSH atomically moves the job to the processing queue.
	msg, err := queue.ConsumeMessage(ctx)
	if err != nil {
		return err
	}
	if msg == nil {
		return nil
	}

	videoID := msg.VideoID
	// Every log line of this job — worker frame and pipeline steps alike — carries these
	// fields, so concurrent jobs stay distinguishable when WORKER_COUNT > 1.
	jobFields := log.With().Int("workerID", workerID).Str("videoID", videoID)
	// The API stamped the ID of the request that enqueued this job. Carrying it here is the
	// whole point of the field: it is what ties the user's upload to these lines in Loki.
	// A job published without one (older job, or a producer that does not set it) just logs
	// without the field — it must not stop the job.
	if publishedState, err := queue.GetJobState(ctx, videoID); err == nil && publishedState != nil {
		if publishedState.CorrelationID != "" {
			jobFields = jobFields.Str("correlationID", publishedState.CorrelationID)
		}
	}
	jobLogger := jobFields.Logger()
	ctx = jobLogger.WithContext(ctx)
	jobLogger.Info().Msg("Processing video")

	// Bookkeeping (job state, ack, DLQ) must outlive the job budget and SIGTERM: if the
	// heavy work is canceled, these writes are exactly what has to still happen, or the job
	// stays in the :processing queue forever. WithoutCancel keeps the values — the job
	// logger included — and drops only the cancellation.
	bookkeepingCtx, cancelBookkeeping := context.WithTimeout(
		context.WithoutCancel(ctx), bookkeepingTimeout)
	defer cancelBookkeeping()

	if err := queue.SetJobProcessing(bookkeepingCtx, videoID); err != nil {
		jobLogger.Warn().Err(err).Msg("Failed to update job state to processing")
	}

	// Root job span — covers the entire processing including upload
	jobCtx, span := telemetry.Tracer().Start(ctx, "process_job",
		oteltrace.WithAttributes(attribute.String("video.id", videoID)),
	)
	defer span.End()

	processCtx, cancel := context.WithTimeout(jobCtx, jobTimeout(cfg))
	defer cancel()

	done := make(chan error, 1)

	go func() {
		// jobErr tracks the final error for the defer below.
		var jobErr error

		metrics.ActiveWorkers.Inc()
		defer metrics.ActiveWorkers.Dec()

		defer func() {
			if jobErr != nil {
				state, err := queue.SetJobFailed(bookkeepingCtx, videoID, jobErr)
				if err != nil {
					jobLogger.Warn().Err(err).Msg("Failed to update job state to failed")
				}
				if state != nil && state.ShouldRetry() {
					if err := queue.RequeueJob(bookkeepingCtx, videoID); err != nil {
						jobLogger.Warn().Err(err).Msg("Failed to requeue job")
					} else {
						jobLogger.Warn().Int("attempt", state.RetryCount).Int("max", queue.MaxJobRetries).Msg("Job scheduled for retry")
					}
				} else {
					if err := queue.MoveToDLQ(bookkeepingCtx, videoID); err != nil {
						jobLogger.Warn().Err(err).Msg("Failed to move job to dead letter queue")
					} else {
						jobLogger.Error().Str("error", jobErr.Error()).Msg("Job moved to dead letter queue after exhausting retries")
						// Notify the API about the permanent failure (retries exhausted)
						if state != nil && state.CallbackURL != "" {
							//nolint:contextcheck // detached on purpose: the notification must survive the job
							// context being canceled — bounded by the webhook client's own 10s timeout (webhook.send)
							go notifyWebhook(state.CallbackURL, cfg.WebhookSecret, videoID, state)
						}
					}
				}
			}
			if err := queue.AcknowledgeMessage(bookkeepingCtx, videoID); err != nil {
				jobLogger.Warn().Err(err).Msg("Failed to acknowledge job")
			}
		}()

		startTime := time.Now()

		inputPath := filepath.Join(os.TempDir(), videoID+"_input.mp4")
		outputPath := filepath.Join(os.TempDir(), videoID+"_output.mp4")

		defer func() {
			// Best-effort cleanup: the job is over either way, and a leftover temp file
			// is a disk-space problem, not a job outcome.
			_ = os.Remove(inputPath)
			_ = os.Remove(outputPath)
		}()

		if err := minio.DownloadVideo(processCtx, minio.VideoTypeRaw, videoID, inputPath); err != nil {
			jobErr = fmt.Errorf("failed to download video: %w", err)
			metrics.VideosProcessedTotal.WithLabelValues("error").Inc()
			done <- jobErr
			return
		}
		if info, err := os.Stat(inputPath); err == nil {
			metrics.VideoSizeBytes.Observe(float64(info.Size()))
		}

		result, err := processor.ProcessVideo(processCtx, inputPath, outputPath, processor.Options{
			ParallelNonCriticalSteps:      cfg.ParallelNonCriticalSteps,
			MaxParallelPostTranscodeSteps: cfg.MaxParallelPostTranscodeSteps,
			HLSSingleCommand:              cfg.HLSSingleCommand,
			HLSSingleCommandFallback:      cfg.HLSSingleCommandFallback,
			VideoEncoder:                  videoEncoder,
			NVENCPreset:                   cfg.NVENCPreset,
			TimeoutScale:                  cfg.ProcessingTimeoutScale,
		})
		if result != nil {
			defer func() { _ = os.RemoveAll(result.TempDir) }()
		}
		if err != nil {
			jobErr = fmt.Errorf("failed to process video: %w", err)
			metrics.VideosProcessedTotal.WithLabelValues("error").Inc()
			done <- jobErr
			return
		}

		processedID := videoID + "_processed"
		if err := minio.UploadVideo(processCtx, outputPath, minio.VideoTypeProcessed, processedID); err != nil {
			jobErr = fmt.Errorf("failed to upload video: %w", err)
			metrics.VideosProcessedTotal.WithLabelValues("error").Inc()
			done <- jobErr
			return
		}

		// Archive the original raw to raw-archived/ (auto-deleted after 30 days).
		// Error is not fatal — the video is already processed and artifacts are in MinIO.
		if err := minio.ArchiveRawVideo(processCtx, videoID); err != nil {
			jobLogger.Warn().Err(err).Msg("Failed to archive raw — will be retained in raw/")
		}

		// Upload optional artifacts generated by the pipeline
		if result.ThumbnailsDir != "" {
			if err := minio.UploadDirectory(processCtx, result.ThumbnailsDir, "thumbnails/"+videoID); err != nil {
				jobLogger.Warn().Err(err).Msg("Failed to upload thumbnails")
			}
		}
		if result.AudioPath != "" {
			if err := minio.UploadFile(processCtx, result.AudioPath, "audio/"+videoID+".mp3"); err != nil {
				jobLogger.Warn().Err(err).Msg("Failed to upload audio")
			}
		}
		if result.PreviewPath != "" {
			if err := minio.UploadFile(processCtx, result.PreviewPath, "preview/"+videoID+"_preview.mp4"); err != nil {
				jobLogger.Warn().Err(err).Msg("Failed to upload preview")
			}
		}
		if result.StreamingDir != "" {
			if err := minio.UploadDirectory(processCtx, result.StreamingDir, "hls/"+videoID); err != nil {
				jobLogger.Warn().Err(err).Msg("Failed to upload HLS segments")
			}
		}

		if err := queue.PublishSuccessMessage(processCtx, processedID); err != nil {
			jobErr = fmt.Errorf("failed to publish success message: %w", err)
			metrics.VideosProcessedTotal.WithLabelValues("error").Inc()
			done <- jobErr
			return
		}

		// Record final state and success metrics
		artifacts := buildJobArtifacts(videoID, processedID, result)
		metadata := toJobMetadata(result)
		if err := queue.SetJobDone(bookkeepingCtx, videoID, artifacts, metadata); err != nil {
			jobLogger.Warn().Err(err).Msg("Failed to update job state to done")
		}

		// Notify the API about success
		if state, err := queue.GetJobState(bookkeepingCtx, videoID); err == nil && state != nil && state.CallbackURL != "" {
			//nolint:contextcheck // detached on purpose: the notification must survive the job
			// context being canceled — bounded by the webhook client's own 10s timeout (webhook.send)
			go notifyWebhook(state.CallbackURL, cfg.WebhookSecret, videoID, state)
		}

		duration := time.Since(startTime).Seconds()
		metrics.ProcessingDuration.Observe(duration)
		metrics.VideosProcessedTotal.WithLabelValues("success").Inc()

		jobLogger.Info().Float64("duration_seconds", duration).Msg("Video processed successfully")
		done <- nil
	}()

	select {
	case err := <-done:
		return err
	case <-processCtx.Done():
		return fmt.Errorf("operation canceled: %w", processCtx.Err())
	}
}

// toJobMetadata converts pipeline metadata to the queue package type.
func toJobMetadata(result *processor.ProcessingResult) *queue.VideoMetadata {
	if result.Metadata == nil {
		return nil
	}
	return &queue.VideoMetadata{
		Duration:   result.Metadata.Duration,
		Width:      result.Metadata.Width,
		Height:     result.Metadata.Height,
		VideoCodec: result.Metadata.VideoCodec,
		AudioCodec: result.Metadata.AudioCodec,
		FPS:        result.Metadata.FPS,
		Bitrate:    result.Metadata.Bitrate,
		Size:       result.Metadata.Size,
	}
}

// buildJobArtifacts builds the artifacts object with MinIO paths
// from the pipeline result. Only includes artifacts that were generated.
func buildJobArtifacts(videoID, processedID string, result *processor.ProcessingResult) queue.JobArtifacts {
	artifacts := queue.JobArtifacts{
		Video: "processed/" + processedID,
	}
	if result.ThumbnailsDir != "" {
		artifacts.Thumbnails = "thumbnails/" + videoID
	}
	if result.AudioPath != "" {
		artifacts.Audio = "audio/" + videoID + ".mp3"
	}
	if result.PreviewPath != "" {
		artifacts.Preview = "preview/" + videoID + "_preview.mp4"
	}
	if result.StreamingDir != "" {
		artifacts.HLS = "hls/" + videoID
	}
	return artifacts
}

// buildWebhookPayload maps the job state to the `video-processed` webhook body.
// The shape is a contract with the API: see contracts/video-processed-*.json, which both
// sides test against. Optional artifacts and the whole metadata block may be absent —
// a non-critical step failing still reports success.
func buildWebhookPayload(videoID string, state *queue.JobState) webhook.Payload {
	success := state.Status == queue.JobStatusDone

	payload := webhook.Payload{
		VideoID: videoID,
		Success: success,
	}

	if success && state.Artifacts != nil {
		payload.ProcessedPath = state.Artifacts.Video
		payload.PreviewPath = state.Artifacts.Preview
		payload.HlsPath = state.Artifacts.HLS
		payload.AudioPath = state.Artifacts.Audio

		if state.Artifacts.Thumbnails != "" {
			paths := make([]string, 5)
			for i := 1; i <= 5; i++ {
				paths[i-1] = fmt.Sprintf("%s/thumb_%03d.jpg", state.Artifacts.Thumbnails, i)
			}
			payload.ThumbnailPaths = paths
		}
	}

	if success && state.Metadata != nil {
		size := state.Metadata.Size
		duration := state.Metadata.Duration
		width := state.Metadata.Width
		height := state.Metadata.Height

		payload.FileSizeBytes = &size
		payload.DurationSeconds = &duration
		payload.Width = &width
		payload.Height = &height
		payload.Codec = state.Metadata.VideoCodec
	}

	return payload
}

// notifyWebhook sends the job completion notification to the callbackURL in the background.
// Delivery errors are only logged — they do not affect the job result.
func notifyWebhook(callbackURL, secret, videoID string, state *queue.JobState) {
	payload := buildWebhookPayload(videoID, state)

	if err := webhook.Notify(callbackURL, secret, state.CorrelationID, payload); err != nil {
		log.Warn().Err(err).Str("videoID", videoID).Str("callbackURL", callbackURL).Msg("Failed to send webhook")
	} else {
		log.Info().Str("videoID", videoID).Str("callbackURL", callbackURL).Msg("Webhook sent successfully")
	}
}
