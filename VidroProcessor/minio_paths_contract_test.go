package main

import (
	"bytes"
	"encoding/json"
	"os"
	"path/filepath"
	"reflect"
	"strings"
	"testing"

	"video-processor/internal/processor"
	processor_steps "video-processor/internal/processor/processor-steps"
	"video-processor/internal/worker"
	"video-processor/minio"
	"video-processor/queue"
)

// The MinIO object layout is a contract with VidroApi: the API writes raw/<id>, this worker
// downloads it, uploads the artifacts and reports those paths on the webhook. Until this
// golden, the layout was only documented. MinioPathsContractTests.cs proves the API writes
// and parses the same keys; this proves the worker reads and writes them.

type minioPathsGolden struct {
	VideoID          string   `json:"videoId"`
	RawPrefix        string   `json:"rawPrefix"`
	Raw              string   `json:"raw"`
	RawArchived      string   `json:"rawArchived"`
	Processed        string   `json:"processed"`
	Audio            string   `json:"audio"`
	Preview          string   `json:"preview"`
	ThumbnailsPrefix string   `json:"thumbnailsPrefix"`
	ThumbnailCount   int      `json:"thumbnailCount"`
	ThumbnailPaths   []string `json:"thumbnailPaths"`
	CustomThumbnail  string   `json:"customThumbnail"`
	HlsPrefix        string   `json:"hlsPrefix"`
}

func loadMinioPathsGolden(t *testing.T) minioPathsGolden {
	t.Helper()
	decoder := json.NewDecoder(bytes.NewReader(contractGolden(t, "minio-paths.json")))
	decoder.DisallowUnknownFields()
	var golden minioPathsGolden
	if err := decoder.Decode(&golden); err != nil {
		t.Fatalf("failed to parse golden: %v", err)
	}
	return golden
}

func TestMinioPaths_MatchContractGolden(t *testing.T) {
	golden := loadMinioPathsGolden(t)
	if golden.VideoID != contractVideoID {
		t.Fatalf("golden videoId = %q, want the shared placeholder %q", golden.VideoID, contractVideoID)
	}

	assertPath := func(name, got, want string) {
		t.Helper()
		if got != want {
			t.Errorf("%s = %q, contracts/minio-paths.json says %q", name, got, want)
		}
	}

	assertPath("RawPrefix", string(minio.VideoTypeRaw), golden.RawPrefix)
	assertPath("RawObject", minio.RawObject(golden.VideoID), golden.Raw)
	assertPath("RawArchivedObject", minio.RawArchivedObject(golden.VideoID), golden.RawArchived)
	assertPath("ProcessedObject", minio.ProcessedObject(golden.VideoID), golden.Processed)
	assertPath("AudioObject", minio.AudioObject(golden.VideoID), golden.Audio)
	assertPath("PreviewObject", minio.PreviewObject(golden.VideoID), golden.Preview)
	assertPath("ThumbnailsPrefix", minio.ThumbnailsPrefix(golden.VideoID), golden.ThumbnailsPrefix)
	assertPath("HLSPrefix", minio.HLSPrefix(golden.VideoID), golden.HlsPrefix)

	if processor_steps.ThumbnailCount != golden.ThumbnailCount {
		t.Errorf("ThumbnailCount = %d, contracts/minio-paths.json says %d",
			processor_steps.ThumbnailCount, golden.ThumbnailCount)
	}

	builtThumbnailPaths := make([]string, golden.ThumbnailCount)
	for i := 1; i <= golden.ThumbnailCount; i++ {
		builtThumbnailPaths[i-1] = minio.ThumbnailsPrefix(golden.VideoID) + "/" + processor_steps.ThumbnailFileName(i)
	}
	if !reflect.DeepEqual(builtThumbnailPaths, golden.ThumbnailPaths) {
		t.Errorf("thumbnailPaths = %v, contracts/minio-paths.json says %v",
			builtThumbnailPaths, golden.ThumbnailPaths)
	}

	wantCustom := golden.ThumbnailsPrefix + "/custom.jpg"
	if golden.CustomThumbnail != wantCustom {
		t.Errorf("customThumbnail = %q, want it under the worker's thumbnails prefix (%q)",
			golden.CustomThumbnail, wantCustom)
	}

	result := &processor.ProcessingResult{
		ThumbnailsDir: "thumbs",
		AudioPath:     "audio.mp3",
		PreviewPath:   "preview.mp4",
		StreamingDir:  "hls",
	}
	artifacts := worker.BuildJobArtifacts(golden.VideoID, result)
	assertPath("BuildJobArtifacts.Video", artifacts.Video, golden.Processed)
	assertPath("BuildJobArtifacts.Thumbnails", artifacts.Thumbnails, golden.ThumbnailsPrefix)
	assertPath("BuildJobArtifacts.Audio", artifacts.Audio, golden.Audio)
	assertPath("BuildJobArtifacts.Preview", artifacts.Preview, golden.Preview)
	assertPath("BuildJobArtifacts.HLS", artifacts.HLS, golden.HlsPrefix)

	payload := worker.BuildWebhookPayload(golden.VideoID, &queue.JobState{
		Status:    queue.JobStatusDone,
		Artifacts: &artifacts,
	})
	if !reflect.DeepEqual(payload.ThumbnailPaths, golden.ThumbnailPaths) {
		t.Errorf("BuildWebhookPayload thumbnailPaths = %v, contracts/minio-paths.json says %v",
			payload.ThumbnailPaths, golden.ThumbnailPaths)
	}
}

// The MinIO notification that wakes the API is configured in the stack, not in this worker.
// The prefix must stay the raw/ the API writes and this worker downloads — a different one
// means every upload is silent.
func TestMinioNotifyPrefix_MatchesContractGolden(t *testing.T) {
	golden := loadMinioPathsGolden(t)
	wantPrefix := golden.RawPrefix + "/"

	composePath := filepath.Join("..", "docker-compose.yml")
	content, err := os.ReadFile(composePath)
	if err != nil {
		t.Fatalf("failed to read %s: %v", composePath, err)
	}
	if !strings.Contains(string(content), "--prefix "+wantPrefix) {
		t.Errorf("%s does not notify on --prefix %s, contracts/minio-paths.json says rawPrefix=%q",
			composePath, wantPrefix, golden.RawPrefix)
	}
}
