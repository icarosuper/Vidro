package processor_steps

import (
	"context"
	"os"
	"path/filepath"
	"testing"
)

func TestGeneratePreview_ValidVideo(t *testing.T) {
	inputPath := GenerateTestVideo(t, 5)
	outputPath := filepath.Join(t.TempDir(), "preview.mp4")

	if err := GeneratePreview(context.Background(), inputPath, outputPath); err != nil {
		t.Fatalf("GeneratePreview() failed: %v", err)
	}

	info, err := os.Stat(outputPath)
	if os.IsNotExist(err) {
		t.Fatal("GeneratePreview() did not create the output file")
	}
	if info.Size() == 0 {
		t.Error("GeneratePreview() created an empty preview file")
	}

	if err := ValidateVideo(context.Background(), outputPath); err != nil {
		t.Errorf("GeneratePreview() produced an invalid video: %v", err)
	}
}

func TestGeneratePreview_InvalidVideo(t *testing.T) {
	invalidPath := CreateInvalidFile(t)
	outputPath := filepath.Join(t.TempDir(), "preview.mp4")

	if err := GeneratePreview(context.Background(), invalidPath, outputPath); err == nil {
		t.Error("GeneratePreview() should fail with invalid input")
	}
}

func TestGeneratePreview_NonExistentVideo(t *testing.T) {
	outputPath := filepath.Join(t.TempDir(), "preview.mp4")

	if err := GeneratePreview(context.Background(), "/path/that/does/not/exist.mp4", outputPath); err == nil {
		t.Error("GeneratePreview() should fail with non-existent file")
	}
}
