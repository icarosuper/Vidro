package processor_steps

import (
	"context"
	"fmt"
	"os/exec"
)

// ExtractAudio extracts the audio track from the video in MP3 format.
func ExtractAudio(ctx context.Context, inputPath, outputPath string) error {
	cmd := exec.CommandContext(ctx, "ffmpeg",
		"-i", inputPath,
		"-vn",
		"-acodec", "libmp3lame",
		"-ab", "192k",
		"-y",
		outputPath,
	)

	output, err := cmd.CombinedOutput()
	if err != nil {
		return fmt.Errorf("audio extraction failed: %w, output: %s", err, string(output))
	}

	return nil
}
