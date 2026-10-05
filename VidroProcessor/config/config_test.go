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
