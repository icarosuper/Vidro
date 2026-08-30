package processor_steps

import (
	"context"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strconv"
	"strings"
)

// ThumbnailConfig defines the configuration for thumbnail generation.
type ThumbnailConfig struct {
	Count  int
	Width  int
	Height int
}

// GenerateThumbnails generates thumbnails from the video at multiple timestamps.
func GenerateThumbnails(ctx context.Context, inputPath, outputDir string) error {
	config := ThumbnailConfig{Count: 5, Width: 320, Height: 180}

	if err := os.MkdirAll(outputDir, 0755); err != nil {
		return fmt.Errorf("failed to create thumbnails directory: %w", err)
	}

	durationCmd := exec.CommandContext(ctx, "ffprobe",
		"-v", "error",
		"-show_entries", "format=duration",
		"-of", "default=noprint_wrappers=1:nokey=1",
		inputPath,
	)

	durationOutput, err := durationCmd.Output()
	if err != nil {
		return fmt.Errorf("failed to get duration: %w", err)
	}

	duration, err := strconv.ParseFloat(strings.TrimSpace(string(durationOutput)), 64)
	if err != nil {
		return fmt.Errorf("invalid duration: %w", err)
	}

	interval := duration / float64(config.Count+1)

	for i := 1; i <= config.Count; i++ {
		timestamp := interval * float64(i)
		thumbnailPath := filepath.Join(outputDir, fmt.Sprintf("thumb_%03d.jpg", i))

		cmd := exec.CommandContext(ctx, "ffmpeg",
			"-ss", strconv.FormatFloat(timestamp, 'f', 2, 64),
			"-i", inputPath,
			"-vframes", "1",
			"-vf", fmt.Sprintf("scale=%d:%d", config.Width, config.Height),
			"-y",
			thumbnailPath,
		)

		if err := cmd.Run(); err != nil {
			return fmt.Errorf("failed to generate thumbnail %d: %w", i, err)
		}
	}

	return nil
}
