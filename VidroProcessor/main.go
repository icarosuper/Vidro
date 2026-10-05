package main

import (
	"context"
	"io"
	"net/http"
	"os"
	"os/signal"
	"runtime"
	"sync"
	"syscall"
	"time"

	"github.com/prometheus/client_golang/prometheus/promhttp"
	"github.com/rs/zerolog"
	"github.com/rs/zerolog/log"

	"video-processor/config"
	"video-processor/internal/processor"
	processor_steps "video-processor/internal/processor/processor-steps"
	"video-processor/internal/telemetry"
	"video-processor/internal/worker"
	"video-processor/metrics"
	"video-processor/minio"
	"video-processor/queue"
)

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
		Dur("jobTimeout", worker.JobTimeout(cfg)).
		Msg("Starting video-processor")

	ctx, cancel := context.WithCancel(context.Background())

	// Goroutine that re-queues orphan jobs (crash during processing). The threshold
	// must stay above the job budget, otherwise a healthy long job gets requeued
	// while it is still running.
	go queue.StartRecovery(ctx, worker.JobTimeout(cfg)+time.Minute)

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
	jobWorker := worker.New(cfg, videoEncoder)
	for i := range numWorkers {
		wg.Add(1)
		go func(workerID int) {
			defer wg.Done()
			jobWorker.Run(ctx, workerID)
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
