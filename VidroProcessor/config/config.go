package config

import (
	"errors"
	"log"
	"os"
	"time"

	"github.com/caarlos0/env/v10"
	"github.com/joho/godotenv"
)

type Config struct {
	// Redis
	RedisHost               string `env:"REDIS_HOST,notEmpty"`
	ProcessingRequestQueue  string `env:"PROCESSING_REQUEST_QUEUE,notEmpty"`
	ProcessingFinishedQueue string `env:"PROCESSING_FINISHED_QUEUE,notEmpty"`

	// MinIO
	MinioEndpoint     string `env:"MINIO_ENDPOINT,notEmpty"`
	MinioRootUser     string `env:"MINIO_ROOT_USER,notEmpty"`
	MinioRootPassword string `env:"MINIO_ROOT_PASSWORD,notEmpty"`
	MinioBucketName   string `env:"MINIO_BUCKET_NAME,notEmpty"`
	MinioUseSSL       bool   `env:"MINIO_USE_SSL" envDefault:"false"`

	// HTTP
	HTTPPort string `env:"HTTP_PORT" envDefault:"8080"`

	// Observability
	OTelEndpoint    string `env:"OTEL_ENDPOINT"`                                  // optional: OTLP endpoint (e.g. jaeger:4318); empty = no-op
	OTelServiceName string `env:"OTEL_SERVICE_NAME" envDefault:"video-processor"` // service name in traces

	// Webhook
	WebhookSecret string `env:"WEBHOOK_SECRET"` // optional: signs requests with HMAC-SHA256

	// Workers
	WorkerCount int `env:"WORKER_COUNT" envDefault:"0"`

	// Processing
	MaxFileSizeMB                 int64 `env:"MAX_FILE_SIZE_MB" envDefault:"5120"` // 5 GB
	ParallelNonCriticalSteps      bool  `env:"PARALLEL_NON_CRITICAL_STEPS" envDefault:"true"`
	MaxParallelPostTranscodeSteps int   `env:"MAX_PARALLEL_POST_TRANSCODE_STEPS" envDefault:"4"`
	HLSSingleCommand              bool  `env:"HLS_SINGLE_COMMAND" envDefault:"true"`
	HLSSingleCommandFallback      bool  `env:"HLS_SINGLE_COMMAND_FALLBACK" envDefault:"true"`
	// VideoEncoder: auto (probe NVENC), nvenc (GPU if available, else CPU), cpu (libx264 only).
	VideoEncoder string `env:"VIDEO_ENCODER" envDefault:"auto"`
	// NVENCPreset: FFmpeg NVENC preset p1–p7 (Turing+). p5 is a good default for 1080p quality.
	NVENCPreset string `env:"NVENC_PRESET" envDefault:"p5"`
	// ProcessingTimeoutScale multiplies every step timeout and the derived whole-job
	// budget. Encoding time depends on the host CPU/GPU and on the input file, so this
	// is the calibration knob: 2 doubles every timeout on a slow host.
	ProcessingTimeoutScale float64 `env:"PROCESSING_TIMEOUT_SCALE" envDefault:"1"`
	// JobTimeout overrides the whole-job budget. 0 derives it from the step timeouts
	// (processor.JobBudget) — keep it 0 unless you know better.
	JobTimeout time.Duration `env:"JOB_TIMEOUT" envDefault:"0"`
}

func LoadConfig() *Config {
	// Ignore error if file does not exist (e.g. Docker deploy with injected env vars)
	if err := godotenv.Load(); err != nil && !errors.Is(err, os.ErrNotExist) {
		log.Fatalf("failed to load .env: %v", err)
	}

	cfg := Config{}
	if err := env.Parse(&cfg); err != nil {
		log.Fatalf("failed to read environment variables: %v", err)
	}

	log.Printf("Configuration loaded: redis=%s minio=%s bucket=%s workers=%d",
		cfg.RedisHost, cfg.MinioEndpoint, cfg.MinioBucketName, cfg.WorkerCount)

	return &cfg
}
