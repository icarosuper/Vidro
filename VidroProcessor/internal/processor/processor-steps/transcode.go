package processor_steps

import (
	"context"
	"fmt"
	"os/exec"
	"strings"

	"github.com/rs/zerolog/log"
)

// TranscodeVideo converts the video to standardized formats (MP4, H.264, AAC).
// encoder is VideoEncoderCPU or VideoEncoderNVENC; nvencPreset is used only for NVENC (e.g. p4–p7).
func TranscodeVideo(ctx context.Context, inputPath, outputPath, encoder, nvencPreset string) error {
	switch strings.ToLower(strings.TrimSpace(encoder)) {
	case VideoEncoderNVENC:
		preset := NormalizeNVENCPreset(nvencPreset)
		if err := transcodeVideoNVENC(ctx, inputPath, outputPath, preset); err != nil {
			log.Warn().Err(err).Msg("NVENC transcode failed, falling back to CPU (libx264)")
			return transcodeVideoCPU(ctx, inputPath, outputPath)
		}
		return nil
	default:
		return transcodeVideoCPU(ctx, inputPath, outputPath)
	}
}

func transcodeVideoCPU(ctx context.Context, inputPath, outputPath string) error {
	cmd := exec.CommandContext(ctx, "ffmpeg",
		"-i", inputPath,
		"-c:v", "libx264",
		"-preset", "fast",
		"-crf", "23",
		"-c:a", "aac",
		"-b:a", "128k",
		"-movflags", "+faststart",
		"-y",
		outputPath,
	)
	output, err := cmd.CombinedOutput()
	if err != nil {
		return fmt.Errorf("transcoding failed: %w, output: %s", err, string(output))
	}
	return nil
}

func transcodeVideoNVENC(ctx context.Context, inputPath, outputPath, preset string) error {
	args := []string{
		"-hwaccel", "cuda",
		"-i", inputPath,
		"-c:v", "h264_nvenc",
		"-preset", preset,
		"-tune", "hq",
		"-rc", "vbr",
		"-cq", "23",
		"-c:a", "aac",
		"-b:a", "128k",
		"-movflags", "+faststart",
		"-y", outputPath,
	}
	cmd := exec.CommandContext(ctx, "ffmpeg", args...)
	out, err := cmd.CombinedOutput()
	if err == nil {
		return nil
	}
	firstErr := fmt.Errorf("nvenc (cuda decode): %w, output: %s", err, string(out))

	log.Warn().Err(firstErr).Msg("NVENC transcode with -hwaccel cuda failed, retrying without CUDA decode")

	args = []string{
		"-i", inputPath,
		"-c:v", "h264_nvenc",
		"-preset", preset,
		"-tune", "hq",
		"-rc", "vbr",
		"-cq", "23",
		"-c:a", "aac",
		"-b:a", "128k",
		"-movflags", "+faststart",
		"-y", outputPath,
	}
	cmd = exec.CommandContext(ctx, "ffmpeg", args...)
	out, err = cmd.CombinedOutput()
	if err != nil {
		return fmt.Errorf("nvenc (no hwaccel): %w, output: %s", err, string(out))
	}
	return nil
}
