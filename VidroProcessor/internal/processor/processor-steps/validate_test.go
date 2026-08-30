package processor_steps

import (
	"context"
	"os"
	"path/filepath"
	"testing"
)

func TestValidateVideo_ValidVideo(t *testing.T) {
	videoPath := GenerateTestVideo(t, 5)

	err := ValidateVideo(context.Background(), videoPath)
	if err != nil {
		t.Errorf("ValidateVideo() should succeed with valid video, but returned error: %v", err)
	}
}

func TestValidateVideo_InvalidVideo(t *testing.T) {
	invalidPath := CreateInvalidFile(t)

	err := ValidateVideo(context.Background(), invalidPath)
	if err == nil {
		t.Error("ValidateVideo() should fail with invalid file, but succeeded")
	}
}

func TestValidateVideo_NonExistentFile(t *testing.T) {
	err := ValidateVideo(context.Background(), "/path/that/does/not/exist.mp4")
	if err == nil {
		t.Error("ValidateVideo() should fail with non-existent file, but succeeded")
	}
}

func TestValidateVideo_EmptyFile(t *testing.T) {
	tempDir := t.TempDir()
	emptyPath := filepath.Join(tempDir, "empty.mp4")

	if err := os.WriteFile(emptyPath, []byte{}, 0644); err != nil {
		t.Fatalf("Failed to create empty file: %v", err)
	}

	err := ValidateVideo(context.Background(), emptyPath)
	if err == nil {
		t.Error("ValidateVideo() should fail with empty file, but succeeded")
	}
}
