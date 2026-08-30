package integration

import (
	"context"
	"fmt"
	"os"
	"os/exec"
	"testing"
	"time"

	"github.com/testcontainers/testcontainers-go"
	"github.com/testcontainers/testcontainers-go/modules/minio"
	"github.com/testcontainers/testcontainers-go/modules/redis"
)

// isDockerAvailable checks if Docker is available and running
func isDockerAvailable() bool {
	cmd := exec.Command("docker", "info")
	cmd.Stdout = nil
	cmd.Stderr = nil
	return cmd.Run() == nil
}

// TestContainers holds references to the test containers
type TestContainers struct {
	Redis      *redis.RedisContainer
	Minio      *minio.MinioContainer
	RedisHost  string
	MinioHost  string
	MinioUser  string
	MinioPass  string
	BucketName string
}

// Containers holds the shared test containers
var Containers *TestContainers

// SetupContainers initializes Redis and MinIO containers for testing
func SetupContainers(t *testing.T) *TestContainers {
	t.Helper()
	ctx := context.Background()

	// Skip if Docker is not available
	if os.Getenv("SKIP_INTEGRATION_TESTS") == "true" {
		t.Skip("Skipping integration tests (SKIP_INTEGRATION_TESTS=true)")
	}

	// Check if Docker is available
	if !isDockerAvailable() {
		t.Skip("Docker is not available - skipping integration test")
	}

	tc := &TestContainers{
		MinioUser:  "minioadmin",
		MinioPass:  "minioadmin",
		BucketName: "videos",
	}

	// Start Redis container
	redisContainer, err := redis.Run(ctx, "redis:7-alpine")
	if err != nil {
		t.Skipf("Failed to start Redis container (Docker may not be running): %v", err)
	}
	tc.Redis = redisContainer

	redisHost, err := redisContainer.ConnectionString(ctx)
	if err != nil {
		t.Fatalf("Failed to get Redis connection string: %v", err)
	}
	// Remove "redis://" prefix
	tc.RedisHost = redisHost[8:]

	// Start MinIO container
	minioContainer, err := minio.Run(ctx, "minio/minio:latest",
		minio.WithUsername(tc.MinioUser),
		minio.WithPassword(tc.MinioPass),
	)
	if err != nil {
		t.Fatalf("Failed to start MinIO container: %v", err)
	}
	tc.Minio = minioContainer

	minioHost, err := minioContainer.ConnectionString(ctx)
	if err != nil {
		t.Fatalf("Failed to get MinIO connection string: %v", err)
	}
	tc.MinioHost = minioHost

	t.Logf("Redis started at: %s", tc.RedisHost)
	t.Logf("MinIO started at: %s", tc.MinioHost)

	return tc
}

// TeardownContainers stops and removes the test containers
func TeardownContainers(t *testing.T, tc *TestContainers) {
	t.Helper()
	ctx := context.Background()

	if tc.Redis != nil {
		if err := tc.Redis.Terminate(ctx); err != nil {
			t.Logf("Failed to terminate Redis container: %v", err)
		}
	}

	if tc.Minio != nil {
		if err := tc.Minio.Terminate(ctx); err != nil {
			t.Logf("Failed to terminate MinIO container: %v", err)
		}
	}
}

// WaitForContainer waits for a container to be ready
func WaitForContainer(t *testing.T, container testcontainers.Container, timeout time.Duration) error {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), timeout)
	defer cancel()

	state, err := container.State(ctx)
	if err != nil {
		return fmt.Errorf("failed to get container state: %w", err)
	}

	if !state.Running {
		return fmt.Errorf("container is not running")
	}

	return nil
}
