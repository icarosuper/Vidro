package processor_steps

import (
	"context"
	"testing"
)

func TestAnalyzeContent_ValidVideo(t *testing.T) {
	videoPath := GenerateTestVideo(t, 5)

	metadata, err := AnalyzeContent(context.Background(), videoPath)
	if err != nil {
		t.Fatalf("AnalyzeContent() failed: %v", err)
	}
	if metadata == nil {
		t.Fatal("AnalyzeContent() returned nil metadata")
	}
	if metadata.Duration <= 0 {
		t.Errorf("AnalyzeContent() returned invalid duration: %v", metadata.Duration)
	}
	if metadata.Width == 0 || metadata.Height == 0 {
		t.Errorf("AnalyzeContent() returned invalid resolution: %dx%d", metadata.Width, metadata.Height)
	}
	if metadata.VideoCodec == "" {
		t.Error("AnalyzeContent() did not return video codec")
	}
}

func TestAnalyzeContent_InvalidVideo(t *testing.T) {
	invalidPath := CreateInvalidFile(t)

	metadata, err := AnalyzeContent(context.Background(), invalidPath)
	if err == nil {
		t.Error("AnalyzeContent() should fail with invalid video")
	}
	if metadata != nil {
		t.Error("AnalyzeContent() should return nil for invalid video")
	}
}

func TestAnalyzeContent_NonExistentVideo(t *testing.T) {
	metadata, err := AnalyzeContent(context.Background(), "/path/that/does/not/exist.mp4")
	if err == nil {
		t.Error("AnalyzeContent() should fail with non-existent file")
	}
	if metadata != nil {
		t.Error("AnalyzeContent() should return nil for non-existent file")
	}
}
