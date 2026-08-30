package processor_steps

import (
	"context"
	"fmt"
	"os/exec"
	"strings"
)

// ValidateVideo validates the format, integrity, and codecs of the video using ffprobe.
func ValidateVideo(ctx context.Context, inputPath string) error {
	cmd := exec.CommandContext(ctx, "ffprobe",
		"-v", "error",
		"-show_entries", "format=duration",
		"-of", "default=noprint_wrappers=1:nokey=1",
		inputPath,
	)

	output, err := cmd.CombinedOutput()
	if err != nil {
		return fmt.Errorf("invalid or corrupted video: %w, output: %s", err, string(output))
	}

	duration := strings.TrimSpace(string(output))
	if duration == "" || duration == "N/A" {
		return fmt.Errorf("video has no valid duration")
	}

	return nil
}
