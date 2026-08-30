package processor_steps

import (
	"os"
	"os/exec"
	"path/filepath"
	"testing"
)

// GenerateTestVideo creates a test video using FFmpeg for use in tests
func GenerateTestVideo(t *testing.T, duration int) string {
	t.Helper()

	// Create temporary directory
	tempDir := t.TempDir()
	videoPath := filepath.Join(tempDir, "test_video.mp4")

	// Check if FFmpeg is available
	if _, err := exec.LookPath("ffmpeg"); err != nil {
		t.Skip("FFmpeg is not available - skipping test")
	}

	// Generate test video with FFmpeg
	// testsrc: generates a visual test pattern
	// sine: generates test audio
	cmd := exec.Command("ffmpeg",
		"-f", "lavfi",
		"-i", "testsrc=duration=5:size=640x480:rate=30",
		"-f", "lavfi",
		"-i", "sine=frequency=1000:duration=5",
		"-pix_fmt", "yuv420p",
		"-c:v", "libx264",
		"-c:a", "aac",
		"-y",
		videoPath,
	)

	// Run command silently
	cmd.Stderr = nil
	cmd.Stdout = nil

	if err := cmd.Run(); err != nil {
		t.Fatalf("Failed to generate test video: %v", err)
	}

	return videoPath
}

// CreateInvalidFile creates an invalid file for error testing
func CreateInvalidFile(t *testing.T) string {
	t.Helper()

	tempDir := t.TempDir()
	invalidPath := filepath.Join(tempDir, "invalid.mp4")

	// Create file with invalid content
	if err := os.WriteFile(invalidPath, []byte("not a valid video"), 0644); err != nil {
		t.Fatalf("Failed to create invalid file: %v", err)
	}

	return invalidPath
}
