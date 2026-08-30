package integration

import (
	"bytes"
	"context"
	"io"
	"os"
	"os/exec"
	"path/filepath"
	"testing"
	"time"

	"github.com/redis/go-redis/v9"
	"github.com/minio/minio-go/v7"
	"github.com/minio/minio-go/v7/pkg/credentials"

	processor_steps "video-processor/internal/processor/processor-steps"
)

// generateTestVideo creates a test video using FFmpeg
func generateTestVideo(t *testing.T) []byte {
	t.Helper()

	if _, err := exec.LookPath("ffmpeg"); err != nil {
		t.Skip("FFmpeg not available - skipping test")
	}

	tempDir := t.TempDir()
	videoPath := filepath.Join(tempDir, "test.mp4")

	cmd := exec.Command("ffmpeg",
		"-f", "lavfi",
		"-i", "testsrc=duration=3:size=320x240:rate=15",
		"-f", "lavfi",
		"-i", "sine=frequency=1000:duration=3",
		"-pix_fmt", "yuv420p",
		"-c:v", "libx264",
		"-c:a", "aac",
		"-y",
		videoPath,
	)

	cmd.Stderr = nil
	cmd.Stdout = nil

	if err := cmd.Run(); err != nil {
		t.Fatalf("Failed to generate test video: %v", err)
	}

	content, err := os.ReadFile(videoPath)
	if err != nil {
		t.Fatalf("Failed to read test video: %v", err)
	}

	return content
}

func TestPipeline_ValidateStep(t *testing.T) {
	tc := SetupContainers(t)
	defer TeardownContainers(t, tc)

	ctx := context.Background()

	// Create MinIO client
	minioClient, err := minio.New(tc.MinioHost, &minio.Options{
		Creds:  credentials.NewStaticV4(tc.MinioUser, tc.MinioPass, ""),
		Secure: false,
	})
	if err != nil {
		t.Fatalf("Failed to create MinIO client: %v", err)
	}

	bucketName := "videos"
	_ = minioClient.MakeBucket(ctx, bucketName, minio.MakeBucketOptions{})

	// Generate and upload test video
	videoContent := generateTestVideo(t)
	videoID := "validate-test-video"
	objectPath := "raw/" + videoID

	_, err = minioClient.PutObject(ctx, bucketName, objectPath, bytes.NewReader(videoContent), int64(len(videoContent)), minio.PutObjectOptions{
		ContentType: "video/mp4",
	})
	if err != nil {
		t.Fatalf("Failed to upload video: %v", err)
	}

	// Download video to temp file
	tempDir := t.TempDir()
	localPath := filepath.Join(tempDir, "input.mp4")

	obj, err := minioClient.GetObject(ctx, bucketName, objectPath, minio.GetObjectOptions{})
	if err != nil {
		t.Fatalf("Failed to get object: %v", err)
	}

	outFile, err := os.Create(localPath)
	if err != nil {
		t.Fatalf("Failed to create file: %v", err)
	}

	_, err = io.Copy(outFile, obj)
	outFile.Close()
	obj.Close()
	if err != nil {
		t.Fatalf("Failed to copy: %v", err)
	}

	// Run validation step
	err = processor_steps.ValidateVideo(ctx, localPath)
	if err != nil {
		t.Errorf("ValidateVideo failed: %v", err)
	}
}

func TestPipeline_TranscodeStep(t *testing.T) {
	tc := SetupContainers(t)
	defer TeardownContainers(t, tc)

	ctx := context.Background()

	minioClient, err := minio.New(tc.MinioHost, &minio.Options{
		Creds:  credentials.NewStaticV4(tc.MinioUser, tc.MinioPass, ""),
		Secure: false,
	})
	if err != nil {
		t.Fatalf("Failed to create MinIO client: %v", err)
	}

	bucketName := "videos"
	_ = minioClient.MakeBucket(ctx, bucketName, minio.MakeBucketOptions{})

	// Generate and upload test video
	videoContent := generateTestVideo(t)
	videoID := "transcode-test-video"
	rawPath := "raw/" + videoID

	_, err = minioClient.PutObject(ctx, bucketName, rawPath, bytes.NewReader(videoContent), int64(len(videoContent)), minio.PutObjectOptions{
		ContentType: "video/mp4",
	})
	if err != nil {
		t.Fatalf("Failed to upload video: %v", err)
	}

	// Download and transcode
	tempDir := t.TempDir()
	inputPath := filepath.Join(tempDir, "input.mp4")
	outputPath := filepath.Join(tempDir, "output.mp4")

	obj, _ := minioClient.GetObject(ctx, bucketName, rawPath, minio.GetObjectOptions{})
	outFile, _ := os.Create(inputPath)
	io.Copy(outFile, obj)
	outFile.Close()
	obj.Close()

	// Run transcode step
	err = processor_steps.TranscodeVideo(ctx, inputPath, outputPath, processor_steps.VideoEncoderCPU, "")
	if err != nil {
		t.Fatalf("TranscodeVideo failed: %v", err)
	}

	// Verify output exists and is valid
	if _, err := os.Stat(outputPath); os.IsNotExist(err) {
		t.Error("Output file was not created")
	}

	err = processor_steps.ValidateVideo(ctx, outputPath)
	if err != nil {
		t.Errorf("Transcoded video is invalid: %v", err)
	}

	// Upload processed video back to MinIO
	processedContent, _ := os.ReadFile(outputPath)
	processedPath := "processed/" + videoID + "_processed"

	_, err = minioClient.PutObject(ctx, bucketName, processedPath, bytes.NewReader(processedContent), int64(len(processedContent)), minio.PutObjectOptions{
		ContentType: "video/mp4",
	})
	if err != nil {
		t.Fatalf("Failed to upload processed video: %v", err)
	}

	// Verify processed video in MinIO
	stat, err := minioClient.StatObject(ctx, bucketName, processedPath, minio.StatObjectOptions{})
	if err != nil {
		t.Errorf("Processed video not found in MinIO: %v", err)
	}

	t.Logf("Processed video size: %d bytes", stat.Size)
}

func TestPipeline_FullWorkflow(t *testing.T) {
	tc := SetupContainers(t)
	defer TeardownContainers(t, tc)

	ctx := context.Background()

	// Create clients
	redisClient := redis.NewClient(&redis.Options{
		Addr: tc.RedisHost,
	})
	defer redisClient.Close()

	minioClient, err := minio.New(tc.MinioHost, &minio.Options{
		Creds:  credentials.NewStaticV4(tc.MinioUser, tc.MinioPass, ""),
		Secure: false,
	})
	if err != nil {
		t.Fatalf("Failed to create MinIO client: %v", err)
	}

	bucketName := "videos"
	_ = minioClient.MakeBucket(ctx, bucketName, minio.MakeBucketOptions{})

	requestQueue := "processing_request_queue"
	successQueue := "processing_finished_queue"

	// Generate and upload test video
	videoContent := generateTestVideo(t)
	videoID := "full-workflow-test"
	rawPath := "raw/" + videoID

	_, err = minioClient.PutObject(ctx, bucketName, rawPath, bytes.NewReader(videoContent), int64(len(videoContent)), minio.PutObjectOptions{
		ContentType: "video/mp4",
	})
	if err != nil {
		t.Fatalf("Failed to upload video: %v", err)
	}

	// Step 1: Add video to request queue
	err = redisClient.LPush(ctx, requestQueue, videoID).Err()
	if err != nil {
		t.Fatalf("Failed to add to request queue: %v", err)
	}

	// Step 2: Consume from queue (simulating worker)
	result, err := redisClient.BRPop(ctx, 5*time.Second, requestQueue).Result()
	if err != nil {
		t.Fatalf("Failed to consume from queue: %v", err)
	}

	consumedVideoID := result[1]
	if consumedVideoID != videoID {
		t.Errorf("Expected videoID '%s', got '%s'", videoID, consumedVideoID)
	}

	// Step 3: Download from MinIO
	tempDir := t.TempDir()
	inputPath := filepath.Join(tempDir, "input.mp4")
	outputPath := filepath.Join(tempDir, "output.mp4")

	downloadPath := "raw/" + consumedVideoID
	obj, _ := minioClient.GetObject(ctx, bucketName, downloadPath, minio.GetObjectOptions{})
	outFile, _ := os.Create(inputPath)
	io.Copy(outFile, obj)
	outFile.Close()
	obj.Close()

	// Step 4: Process video (validate, transcode, analyze)
	if err := processor_steps.ValidateVideo(ctx, inputPath); err != nil {
		t.Fatalf("Validation failed: %v", err)
	}

	if _, err := processor_steps.AnalyzeContent(ctx, inputPath); err != nil {
		t.Logf("Analysis warning: %v", err) // Non-critical
	}

	if err := processor_steps.TranscodeVideo(ctx, inputPath, outputPath, processor_steps.VideoEncoderCPU, ""); err != nil {
		t.Fatalf("Transcode failed: %v", err)
	}

	// Step 5: Upload processed video
	processedContent, _ := os.ReadFile(outputPath)
	processedID := consumedVideoID + "_processed"
	processedPath := "processed/" + processedID

	_, err = minioClient.PutObject(ctx, bucketName, processedPath, bytes.NewReader(processedContent), int64(len(processedContent)), minio.PutObjectOptions{
		ContentType: "video/mp4",
	})
	if err != nil {
		t.Fatalf("Failed to upload processed video: %v", err)
	}

	// Step 6: Add to success queue
	err = redisClient.LPush(ctx, successQueue, processedID).Err()
	if err != nil {
		t.Fatalf("Failed to add to success queue: %v", err)
	}

	// Verify success queue
	successResult, err := redisClient.BRPop(ctx, 5*time.Second, successQueue).Result()
	if err != nil {
		t.Fatalf("Failed to consume from success queue: %v", err)
	}

	if successResult[1] != processedID {
		t.Errorf("Expected processedID '%s', got '%s'", processedID, successResult[1])
	}

	// Verify processed video exists in MinIO
	_, err = minioClient.StatObject(ctx, bucketName, processedPath, minio.StatObjectOptions{})
	if err != nil {
		t.Errorf("Processed video not found: %v", err)
	}

	t.Log("Full workflow completed successfully!")
}

func TestPipeline_ThumbnailGeneration(t *testing.T) {
	tc := SetupContainers(t)
	defer TeardownContainers(t, tc)

	ctx := context.Background()

	minioClient, _ := minio.New(tc.MinioHost, &minio.Options{
		Creds:  credentials.NewStaticV4(tc.MinioUser, tc.MinioPass, ""),
		Secure: false,
	})

	bucketName := "videos"
	_ = minioClient.MakeBucket(ctx, bucketName, minio.MakeBucketOptions{})

	// Generate test video
	videoContent := generateTestVideo(t)
	videoID := "thumbnail-test"

	// Upload
	_, _ = minioClient.PutObject(ctx, bucketName, "raw/"+videoID, bytes.NewReader(videoContent), int64(len(videoContent)), minio.PutObjectOptions{})

	// Download
	tempDir := t.TempDir()
	inputPath := filepath.Join(tempDir, "input.mp4")
	thumbDir := filepath.Join(tempDir, "thumbnails")

	obj, _ := minioClient.GetObject(ctx, bucketName, "raw/"+videoID, minio.GetObjectOptions{})
	outFile, _ := os.Create(inputPath)
	io.Copy(outFile, obj)
	outFile.Close()
	obj.Close()

	// Generate thumbnails
	err := processor_steps.GenerateThumbnails(ctx, inputPath, thumbDir)
	if err != nil {
		t.Fatalf("GenerateThumbnails failed: %v", err)
	}

	// Verify thumbnails
	entries, err := os.ReadDir(thumbDir)
	if err != nil {
		t.Fatalf("Failed to read thumbnail dir: %v", err)
	}

	if len(entries) != 5 {
		t.Errorf("Expected 5 thumbnails, got %d", len(entries))
	}

	// Upload thumbnails to MinIO
	for _, entry := range entries {
		thumbPath := filepath.Join(thumbDir, entry.Name())
		thumbContent, _ := os.ReadFile(thumbPath)

		objectPath := "thumbnails/" + videoID + "/" + entry.Name()
		_, err = minioClient.PutObject(ctx, bucketName, objectPath, bytes.NewReader(thumbContent), int64(len(thumbContent)), minio.PutObjectOptions{
			ContentType: "image/jpeg",
		})
		if err != nil {
			t.Errorf("Failed to upload thumbnail %s: %v", entry.Name(), err)
		}
	}

	t.Log("Thumbnails generated and uploaded successfully!")
}
