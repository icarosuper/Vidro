package processor_steps

import (
	"context"
	"os"
	"path/filepath"
	"testing"
)

func TestExtractAudio_ValidVideo(t *testing.T) {
	inputPath := GenerateTestVideo(t, 5)
	outputPath := filepath.Join(t.TempDir(), "audio.mp3")

	if err := ExtractAudio(context.Background(), inputPath, outputPath); err != nil {
		t.Fatalf("ExtractAudio() failed: %v", err)
	}

	info, err := os.Stat(outputPath)
	if os.IsNotExist(err) {
		t.Fatal("ExtractAudio() did not create the output file")
	}
	if info.Size() == 0 {
		t.Error("ExtractAudio() created an empty audio file")
	}
}

func TestExtractAudio_InvalidVideo(t *testing.T) {
	invalidPath := CreateInvalidFile(t)
	outputPath := filepath.Join(t.TempDir(), "audio.mp3")

	if err := ExtractAudio(context.Background(), invalidPath, outputPath); err == nil {
		t.Error("ExtractAudio() should fail with invalid input")
	}
}

func TestExtractAudio_NonExistentVideo(t *testing.T) {
	outputPath := filepath.Join(t.TempDir(), "audio.mp3")

	if err := ExtractAudio(context.Background(), "/path/that/does/not/exist.mp4", outputPath); err == nil {
		t.Error("ExtractAudio() should fail with non-existent file")
	}
}
