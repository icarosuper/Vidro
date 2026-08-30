package processor_steps

import (
	"context"
	"fmt"
	"os/exec"
	"strconv"
	"strings"
)

// GeneratePreview generates a low-quality preview of the video (first 30 seconds or 10% of the video).
func GeneratePreview(ctx context.Context, inputPath, outputPath string) error {
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

	previewDuration := duration
	if previewDuration > 30 {
		previewDuration = 30
	}

	cmd := exec.CommandContext(ctx, "ffmpeg",
		"-i", inputPath,
		"-t", strconv.FormatFloat(previewDuration, 'f', 0, 64),
		"-vf", "scale=640:-2",
		"-b:v", "500k",
		"-c:a", "aac",
		"-b:a", "64k",
		"-preset", "veryfast",
		"-y",
		outputPath,
	)

	output, err := cmd.CombinedOutput()
	if err != nil {
		return fmt.Errorf("preview generation failed: %w, output: %s", err, string(output))
	}

	return nil
}
