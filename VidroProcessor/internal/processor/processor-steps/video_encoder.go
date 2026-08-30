package processor_steps

import (
	"context"
	"os/exec"
	"strings"

	"github.com/rs/zerolog/log"
)

// Video encoder backends (resolved at runtime for NVENC).
const (
	VideoEncoderCPU   = "cpu"
	VideoEncoderNVENC = "nvenc"
	VideoEncoderAuto  = "auto"
)

// ResolveVideoEncoder maps VIDEO_ENCODER (auto|nvenc|cpu) to an effective backend.
// Probes ffmpeg for h264_nvenc when auto or when nvenc was requested but unavailable.
func ResolveVideoEncoder(ctx context.Context, mode string) string {
	m := strings.ToLower(strings.TrimSpace(mode))
	switch m {
	case "", VideoEncoderAuto:
		if ffmpegListsEncoder(ctx, "h264_nvenc") {
			log.Info().Msg("Video encoder: NVENC (h264_nvenc) — GPU encoding available")
			return VideoEncoderNVENC
		}
		log.Info().Msg("Video encoder: CPU (libx264) — NVENC not available in ffmpeg")
		return VideoEncoderCPU
	case VideoEncoderNVENC:
		if ffmpegListsEncoder(ctx, "h264_nvenc") {
			return VideoEncoderNVENC
		}
		log.Warn().Msg("VIDEO_ENCODER=nvenc but h264_nvenc is not available in ffmpeg; falling back to CPU (libx264)")
		return VideoEncoderCPU
	case VideoEncoderCPU:
		return VideoEncoderCPU
	default:
		log.Warn().Str("VIDEO_ENCODER", mode).Msg("invalid VIDEO_ENCODER, using auto")
		return ResolveVideoEncoder(ctx, VideoEncoderAuto)
	}
}

func ffmpegListsEncoder(ctx context.Context, name string) bool {
	cmd := exec.CommandContext(ctx, "ffmpeg", "-hide_banner", "-encoders")
	out, err := cmd.CombinedOutput()
	if err != nil {
		return false
	}
	return strings.Contains(string(out), name)
}

// NormalizeNVENCPreset returns an FFmpeg NVENC preset (p1–p7). Default p5 balances quality and speed for 1080p.
func NormalizeNVENCPreset(p string) string {
	p = strings.ToLower(strings.TrimSpace(p))
	if p == "" {
		return "p5"
	}
	if len(p) == 2 && p[0] == 'p' && p[1] >= '1' && p[1] <= '7' {
		return p
	}
	log.Warn().Str("NVENC_PRESET", p).Msg("invalid NVENC_PRESET, using p5")
	return "p5"
}
