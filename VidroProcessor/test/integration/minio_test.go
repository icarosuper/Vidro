package integration

import (
	"bytes"
	"context"
	"io"
	"os"
	"path/filepath"
	"testing"

	"github.com/minio/minio-go/v7"
	"github.com/minio/minio-go/v7/pkg/credentials"
)

func TestMinIO_BucketOperations(t *testing.T) {
	tc := SetupContainers(t)
	defer TeardownContainers(t, tc)

	ctx := context.Background()

	// Create MinIO client
	client, err := minio.New(tc.MinioHost, &minio.Options{
		Creds:  credentials.NewStaticV4(tc.MinioUser, tc.MinioPass, ""),
		Secure: false,
	})
	if err != nil {
		t.Fatalf("Failed to create MinIO client: %v", err)
	}

	bucketName := "test-bucket"

	// Test bucket creation
	err = client.MakeBucket(ctx, bucketName, minio.MakeBucketOptions{})
	if err != nil {
		t.Fatalf("Failed to create bucket: %v", err)
	}

	// Verify bucket exists
	exists, err := client.BucketExists(ctx, bucketName)
	if err != nil {
		t.Fatalf("Failed to check bucket existence: %v", err)
	}

	if !exists {
		t.Error("Bucket should exist after creation")
	}

	// List buckets
	buckets, err := client.ListBuckets(ctx)
	if err != nil {
		t.Fatalf("Failed to list buckets: %v", err)
	}

	found := false
	for _, b := range buckets {
		if b.Name == bucketName {
			found = true
			break
		}
	}

	if !found {
		t.Error("Created bucket not found in list")
	}
}

func TestMinIO_ObjectUploadDownload(t *testing.T) {
	tc := SetupContainers(t)
	defer TeardownContainers(t, tc)

	ctx := context.Background()

	client, err := minio.New(tc.MinioHost, &minio.Options{
		Creds:  credentials.NewStaticV4(tc.MinioUser, tc.MinioPass, ""),
		Secure: false,
	})
	if err != nil {
		t.Fatalf("Failed to create MinIO client: %v", err)
	}

	bucketName := "videos"
	objectName := "raw/test-video.mp4"
	testContent := []byte("fake video content for testing")

	// Create bucket
	err = client.MakeBucket(ctx, bucketName, minio.MakeBucketOptions{})
	if err != nil {
		t.Fatalf("Failed to create bucket: %v", err)
	}

	// Upload object
	reader := bytes.NewReader(testContent)
	_, err = client.PutObject(ctx, bucketName, objectName, reader, int64(len(testContent)), minio.PutObjectOptions{
		ContentType: "video/mp4",
	})
	if err != nil {
		t.Fatalf("Failed to upload object: %v", err)
	}

	// Verify object exists
	stat, err := client.StatObject(ctx, bucketName, objectName, minio.StatObjectOptions{})
	if err != nil {
		t.Fatalf("Failed to stat object: %v", err)
	}

	if stat.Size != int64(len(testContent)) {
		t.Errorf("Expected size %d, got %d", len(testContent), stat.Size)
	}

	// Download object
	obj, err := client.GetObject(ctx, bucketName, objectName, minio.GetObjectOptions{})
	if err != nil {
		t.Fatalf("Failed to get object: %v", err)
	}
	defer obj.Close()

	downloadedContent, err := io.ReadAll(obj)
	if err != nil {
		t.Fatalf("Failed to read object: %v", err)
	}

	if !bytes.Equal(downloadedContent, testContent) {
		t.Error("Downloaded content does not match uploaded content")
	}
}

func TestMinIO_VideoWorkflow(t *testing.T) {
	tc := SetupContainers(t)
	defer TeardownContainers(t, tc)

	ctx := context.Background()

	client, err := minio.New(tc.MinioHost, &minio.Options{
		Creds:  credentials.NewStaticV4(tc.MinioUser, tc.MinioPass, ""),
		Secure: false,
	})
	if err != nil {
		t.Fatalf("Failed to create MinIO client: %v", err)
	}

	bucketName := "videos"

	// Create bucket
	err = client.MakeBucket(ctx, bucketName, minio.MakeBucketOptions{})
	if err != nil {
		t.Fatalf("Failed to create bucket: %v", err)
	}

	videoID := "video-123"
	rawPath := "raw/" + videoID
	processedPath := "processed/" + videoID + "_processed"

	rawContent := []byte("raw video data")
	processedContent := []byte("processed video data - larger content after processing")

	// 1. Upload raw video
	_, err = client.PutObject(ctx, bucketName, rawPath, bytes.NewReader(rawContent), int64(len(rawContent)), minio.PutObjectOptions{
		ContentType: "video/mp4",
	})
	if err != nil {
		t.Fatalf("Failed to upload raw video: %v", err)
	}

	// 2. Download raw video (simulating worker download)
	rawObj, err := client.GetObject(ctx, bucketName, rawPath, minio.GetObjectOptions{})
	if err != nil {
		t.Fatalf("Failed to get raw video: %v", err)
	}

	downloadedRaw, err := io.ReadAll(rawObj)
	rawObj.Close()
	if err != nil {
		t.Fatalf("Failed to read raw video: %v", err)
	}

	if !bytes.Equal(downloadedRaw, rawContent) {
		t.Error("Downloaded raw video does not match")
	}

	// 3. Upload processed video
	_, err = client.PutObject(ctx, bucketName, processedPath, bytes.NewReader(processedContent), int64(len(processedContent)), minio.PutObjectOptions{
		ContentType: "video/mp4",
	})
	if err != nil {
		t.Fatalf("Failed to upload processed video: %v", err)
	}

	// 4. Verify both objects exist
	_, err = client.StatObject(ctx, bucketName, rawPath, minio.StatObjectOptions{})
	if err != nil {
		t.Errorf("Raw video not found: %v", err)
	}

	_, err = client.StatObject(ctx, bucketName, processedPath, minio.StatObjectOptions{})
	if err != nil {
		t.Errorf("Processed video not found: %v", err)
	}

	// 5. List objects with prefix
	objectsCh := client.ListObjects(ctx, bucketName, minio.ListObjectsOptions{
		Prefix:    "processed/",
		Recursive: true,
	})

	count := 0
	for obj := range objectsCh {
		if obj.Err != nil {
			t.Errorf("Error listing objects: %v", obj.Err)
			continue
		}
		count++
		t.Logf("Found processed object: %s", obj.Key)
	}

	if count != 1 {
		t.Errorf("Expected 1 processed object, found %d", count)
	}
}

func TestMinIO_DownloadToFile(t *testing.T) {
	tc := SetupContainers(t)
	defer TeardownContainers(t, tc)

	ctx := context.Background()

	client, err := minio.New(tc.MinioHost, &minio.Options{
		Creds:  credentials.NewStaticV4(tc.MinioUser, tc.MinioPass, ""),
		Secure: false,
	})
	if err != nil {
		t.Fatalf("Failed to create MinIO client: %v", err)
	}

	bucketName := "videos"
	objectName := "raw/download-test.mp4"
	testContent := []byte("test video content for file download")

	// Create bucket and upload object
	_ = client.MakeBucket(ctx, bucketName, minio.MakeBucketOptions{})
	_, err = client.PutObject(ctx, bucketName, objectName, bytes.NewReader(testContent), int64(len(testContent)), minio.PutObjectOptions{})
	if err != nil {
		t.Fatalf("Failed to upload object: %v", err)
	}

	// Download to temp file
	tempDir := t.TempDir()
	destPath := filepath.Join(tempDir, "downloaded.mp4")

	obj, err := client.GetObject(ctx, bucketName, objectName, minio.GetObjectOptions{})
	if err != nil {
		t.Fatalf("Failed to get object: %v", err)
	}
	defer obj.Close()

	outFile, err := os.Create(destPath)
	if err != nil {
		t.Fatalf("Failed to create output file: %v", err)
	}

	_, err = io.Copy(outFile, obj)
	outFile.Close()
	if err != nil {
		t.Fatalf("Failed to copy to file: %v", err)
	}

	// Verify file content
	fileContent, err := os.ReadFile(destPath)
	if err != nil {
		t.Fatalf("Failed to read downloaded file: %v", err)
	}

	if !bytes.Equal(fileContent, testContent) {
		t.Error("Downloaded file content does not match")
	}
}

func TestMinIO_NonExistentObject(t *testing.T) {
	tc := SetupContainers(t)
	defer TeardownContainers(t, tc)

	ctx := context.Background()

	client, err := minio.New(tc.MinioHost, &minio.Options{
		Creds:  credentials.NewStaticV4(tc.MinioUser, tc.MinioPass, ""),
		Secure: false,
	})
	if err != nil {
		t.Fatalf("Failed to create MinIO client: %v", err)
	}

	bucketName := "videos"
	_ = client.MakeBucket(ctx, bucketName, minio.MakeBucketOptions{})

	// Try to get non-existent object
	_, err = client.StatObject(ctx, bucketName, "does-not-exist.mp4", minio.StatObjectOptions{})
	if err == nil {
		t.Error("Expected error for non-existent object, got nil")
	}
}
