// Package worker is the job loop: it pulls a videoID off the queue, runs the pipeline and
// closes the job out (state, retry/DLQ, ack, webhook). It lives outside package main so
// tests can drive it — see worker_test.go and design-decisions.md #13.
package worker

import (
	"context"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"time"

	"github.com/rs/zerolog/log"
	"go.opentelemetry.io/otel/attribute"
	oteltrace "go.opentelemetry.io/otel/trace"

	"video-processor/config"
	"video-processor/internal/processor"
	"video-processor/internal/telemetry"
	"video-processor/internal/webhook"
	"video-processor/metrics"
	"video-processor/minio"
	"video-processor/queue"
)

// bookkeepingTimeout caps the Redis writes that close a job out. It is short on purpose:
// these run after cancellation, inside the 30s graceful-shutdown window (design-decisions #12).
const bookkeepingTimeout = 10 * time.Second

// storage is the slice of the minio package a job uses. It exists so tests can swap MinIO
// for a fake; production uses minioStorage.
type storage interface {
	DownloadVideo(ctx context.Context, videoType minio.VideoType, objectID, destPath string) error
	UploadVideo(ctx context.Context, srcPath string, videoType minio.VideoType, objectID string) error
	ArchiveRawVideo(ctx context.Context, videoID string) error
	UploadDirectory(ctx context.Context, srcDir, objectPrefix string) error
	UploadFile(ctx context.Context, srcPath, objectPath string) error
}

// minioStorage forwards to the package-level minio functions.
type minioStorage struct{}

func (minioStorage) DownloadVideo(ctx context.Context, videoType minio.VideoType, objectID, destPath string) error {
	return minio.DownloadVideo(ctx, videoType, objectID, destPath)
}

func (minioStorage) UploadVideo(ctx context.Context, srcPath string, videoType minio.VideoType, objectID string) error {
	return minio.UploadVideo(ctx, srcPath, videoType, objectID)
}

func (minioStorage) ArchiveRawVideo(ctx context.Context, videoID string) error {
	return minio.ArchiveRawVideo(ctx, videoID)
}

func (minioStorage) UploadDirectory(ctx context.Context, srcDir, objectPrefix string) error {
	return minio.UploadDirectory(ctx, srcDir, objectPrefix)
}

func (minioStorage) UploadFile(ctx context.Context, srcPath, objectPath string) error {
	return minio.UploadFile(ctx, srcPath, objectPath)
}

// Worker consumes the processing queue. Redis is reached through the queue package (tests
// point it at miniredis); MinIO and the FFmpeg pipeline are fields so tests can fake them.
type Worker struct {
	cfg          *config.Config
	videoEncoder string
	storage      storage
	processVideo func(ctx context.Context, inputPath, outputPath string, options processor.Options) (*processor.ProcessingResult, error)
}

// New returns a Worker wired to MinIO and the real pipeline. queue.InitRedisClient and
// minio.InitMinioClient must have run.
func New(cfg *config.Config, videoEncoder string) *Worker {
	return &Worker{
		cfg:          cfg,
		videoEncoder: videoEncoder,
		storage:      minioStorage{},
		processVideo: processor.ProcessVideo,
	}
}

// JobTimeout returns the whole-job budget: JOB_TIMEOUT when set, otherwise derived
// from the step timeouts so the budget can never be smaller than the pipeline.
func JobTimeout(cfg *config.Config) time.Duration {
	if cfg.JobTimeout > 0 {
		return cfg.JobTimeout
	}
	return processor.JobBudget(cfg.ProcessingTimeoutScale)
}

// Run processes jobs one after another until ctx is canceled (shutdown).
func (w *Worker) Run(ctx context.Context, workerID int) {
	for {
		select {
		case <-ctx.Done():
			log.Info().Int("workerID", workerID).Msg("Shutting down worker gracefully")
			return
		default:
			if err := w.processNextMessage(ctx, workerID); err != nil {
				if !errors.Is(err, context.Canceled) {
					log.Error().Err(err).Int("workerID", workerID).Msg("Error processing message")
				}
			}
		}
	}
}

func (w *Worker) processNextMessage(ctx context.Context, workerID int) error {
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
	//
	// The release belongs to the goroutine below, NOT to this function: the goroutine sends on
	// `done` from its body and only then runs the defer that closes the job out, so a
	// `defer cancelBookkeeping()` here would cancel that context while those writes are still
	// running — which is exactly the failure WithoutCancel is here to prevent.
	bookkeepingCtx, cancelBookkeeping := context.WithTimeout(
		context.WithoutCancel(ctx), bookkeepingTimeout)

	if err := queue.SetJobProcessing(bookkeepingCtx, videoID); err != nil {
		jobLogger.Warn().Err(err).Msg("Failed to update job state to processing")
	}

	// Root job span — covers the entire processing including upload
	jobCtx, span := telemetry.Tracer().Start(ctx, "process_job",
		oteltrace.WithAttributes(attribute.String("video.id", videoID)),
	)
	defer span.End()

	processCtx, cancel := context.WithTimeout(jobCtx, JobTimeout(w.cfg))
	defer cancel()

	done := make(chan error, 1)

	go func() {
		// jobErr tracks the final error for the defer below.
		var jobErr error

		// Registered first, so it runs last: the bookkeeping context dies only after the
		// writes that close the job out are done with it.
		defer cancelBookkeeping()

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
							go notifyWebhook(state.CallbackURL, w.cfg.WebhookSecret, videoID, state)
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

		if err := w.storage.DownloadVideo(processCtx, minio.VideoTypeRaw, videoID, inputPath); err != nil {
			jobErr = fmt.Errorf("failed to download video: %w", err)
			metrics.VideosProcessedTotal.WithLabelValues("error").Inc()
			done <- jobErr
			return
		}
		if info, err := os.Stat(inputPath); err == nil {
			metrics.VideoSizeBytes.Observe(float64(info.Size()))
		}

		result, err := w.processVideo(processCtx, inputPath, outputPath, processor.Options{
			ParallelNonCriticalSteps:      w.cfg.ParallelNonCriticalSteps,
			MaxParallelPostTranscodeSteps: w.cfg.MaxParallelPostTranscodeSteps,
			HLSSingleCommand:              w.cfg.HLSSingleCommand,
			HLSSingleCommandFallback:      w.cfg.HLSSingleCommandFallback,
			VideoEncoder:                  w.videoEncoder,
			NVENCPreset:                   w.cfg.NVENCPreset,
			TimeoutScale:                  w.cfg.ProcessingTimeoutScale,
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
		if err := w.storage.UploadVideo(processCtx, outputPath, minio.VideoTypeProcessed, processedID); err != nil {
			jobErr = fmt.Errorf("failed to upload video: %w", err)
			metrics.VideosProcessedTotal.WithLabelValues("error").Inc()
			done <- jobErr
			return
		}

		// Archive the original raw to raw-archived/ (auto-deleted after 30 days).
		// Error is not fatal — the video is already processed and artifacts are in MinIO.
		if err := w.storage.ArchiveRawVideo(processCtx, videoID); err != nil {
			jobLogger.Warn().Err(err).Msg("Failed to archive raw — will be retained in raw/")
		}

		// Upload optional artifacts generated by the pipeline
		if result.ThumbnailsDir != "" {
			if err := w.storage.UploadDirectory(processCtx, result.ThumbnailsDir, "thumbnails/"+videoID); err != nil {
				jobLogger.Warn().Err(err).Msg("Failed to upload thumbnails")
			}
		}
		if result.AudioPath != "" {
			if err := w.storage.UploadFile(processCtx, result.AudioPath, "audio/"+videoID+".mp3"); err != nil {
				jobLogger.Warn().Err(err).Msg("Failed to upload audio")
			}
		}
		if result.PreviewPath != "" {
			if err := w.storage.UploadFile(processCtx, result.PreviewPath, "preview/"+videoID+"_preview.mp4"); err != nil {
				jobLogger.Warn().Err(err).Msg("Failed to upload preview")
			}
		}
		if result.StreamingDir != "" {
			if err := w.storage.UploadDirectory(processCtx, result.StreamingDir, "hls/"+videoID); err != nil {
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
			go notifyWebhook(state.CallbackURL, w.cfg.WebhookSecret, videoID, state)
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

// BuildWebhookPayload maps the job state to the `video-processed` webhook body.
// The shape is a contract with the API: see contracts/video-processed-*.json, which both
// sides test against. Optional artifacts and the whole metadata block may be absent —
// a non-critical step failing still reports success.
func BuildWebhookPayload(videoID string, state *queue.JobState) webhook.Payload {
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
	payload := BuildWebhookPayload(videoID, state)

	if err := webhook.Notify(callbackURL, secret, state.CorrelationID, payload); err != nil {
		log.Warn().Err(err).Str("videoID", videoID).Str("callbackURL", callbackURL).Msg("Failed to send webhook")
	} else {
		log.Info().Str("videoID", videoID).Str("callbackURL", callbackURL).Msg("Webhook sent successfully")
	}
}
