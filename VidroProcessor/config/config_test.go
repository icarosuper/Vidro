package config

import (
	"strings"
	"testing"

	"github.com/caarlos0/env/v10"
)

// The API rejects every unsigned video-processed webhook with 401, so a worker without
// WEBHOOK_SECRET would process every job and notify nobody. It must refuse to start instead.
func TestConfig_RequiresWebhookSecret(t *testing.T) {
	t.Setenv("REDIS_HOST", "localhost:6379")
	t.Setenv("PROCESSING_REQUEST_QUEUE", "video_queue")
	t.Setenv("MINIO_ENDPOINT", "localhost:9000")
	t.Setenv("MINIO_ROOT_USER", "minioadmin")
	t.Setenv("MINIO_ROOT_PASSWORD", "minioadmin")
	t.Setenv("MINIO_BUCKET_NAME", "videos")
	t.Setenv("WEBHOOK_SECRET", "")

	err := env.Parse(&Config{})

	rejectedForTheSecret := err != nil && strings.Contains(err.Error(), "WEBHOOK_SECRET")
	if !rejectedForTheSecret {
		t.Fatalf("env.Parse error = %v, want one naming WEBHOOK_SECRET", err)
	}
}

func setRequiredEnv(t *testing.T) {
	t.Helper()
	t.Setenv("REDIS_HOST", "localhost:6379")
	t.Setenv("PROCESSING_REQUEST_QUEUE", "video_queue")
	t.Setenv("MINIO_ENDPOINT", "localhost:9000")
	t.Setenv("MINIO_ROOT_USER", "minioadmin")
	t.Setenv("MINIO_ROOT_PASSWORD", "minioadmin")
	t.Setenv("MINIO_BUCKET_NAME", "videos")
	t.Setenv("WEBHOOK_SECRET", "secret")
}

func TestConfig_NonCriticalStepsDefaultToEnabled(t *testing.T) {
	setRequiredEnv(t)

	cfg := Config{}
	if err := env.Parse(&cfg); err != nil {
		t.Fatalf("env.Parse: %v", err)
	}

	allEnabled := cfg.EnableThumbnails && cfg.EnableAudio && cfg.EnablePreview && cfg.EnableStreaming
	if !allEnabled {
		t.Fatalf("defaults = %+v, want every ENABLE_* true", cfg)
	}
}

func TestConfig_NonCriticalStepsCanBeDisabled(t *testing.T) {
	setRequiredEnv(t)
	t.Setenv("ENABLE_AUDIO", "false")
	t.Setenv("ENABLE_STREAMING", "false")

	cfg := Config{}
	if err := env.Parse(&cfg); err != nil {
		t.Fatalf("env.Parse: %v", err)
	}

	if cfg.EnableAudio || cfg.EnableStreaming || !cfg.EnableThumbnails || !cfg.EnablePreview {
		t.Fatalf("got %+v, want only audio and streaming disabled", cfg)
	}
}
