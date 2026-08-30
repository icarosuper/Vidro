package processor_steps

import (
	"context"
	"os"
	"path/filepath"
	"testing"
)

func TestTranscodeVideo_ValidVideo(t *testing.T) {
	inputPath := GenerateTestVideo(t, 5)
	outputPath := filepath.Join(t.TempDir(), "output.mp4")

	err := TranscodeVideo(context.Background(), inputPath, outputPath, VideoEncoderCPU, "")
	if err != nil {
		t.Fatalf("TranscodeVideo() failed: %v", err)
	}

	if _, err := os.Stat(outputPath); os.IsNotExist(err) {
		t.Error("TranscodeVideo() did not create the output file")
	}

	fileInfo, err := os.Stat(outputPath)
	if err != nil {
		t.Fatalf("Error checking output file: %v", err)
	}
	if fileInfo.Size() == 0 {
		t.Error("TranscodeVideo() created an empty output file")
	}

	if err := ValidateVideo(context.Background(), outputPath); err != nil {
		t.Errorf("TranscodeVideo() produced an invalid video: %v", err)
	}
}

func TestTranscodeVideo_InvalidInput(t *testing.T) {
	invalidPath := CreateInvalidFile(t)
	outputPath := filepath.Join(t.TempDir(), "output.mp4")

	err := TranscodeVideo(context.Background(), invalidPath, outputPath, VideoEncoderCPU, "")
	if err == nil {
		t.Error("TranscodeVideo() should fail with invalid input")
	}
}

func TestTranscodeVideo_NonExistentInput(t *testing.T) {
	outputPath := filepath.Join(t.TempDir(), "output.mp4")

	err := TranscodeVideo(context.Background(), "/path/that/does/not/exist.mp4", outputPath, VideoEncoderCPU, "")
	if err == nil {
		t.Error("TranscodeVideo() should fail with non-existent file")
	}
}
