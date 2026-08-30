package processor_steps

import (
	"context"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"strconv"
	"strings"

	"github.com/rs/zerolog/log"
)

// hlsVariant defines a quality variant for adaptive streaming.
type hlsVariant struct {
	Name         string
	Height       int
	VideoBitrate string
	AudioBitrate string
	Bandwidth    int // bits/s for EXT-X-STREAM-INF
}

// hlsVariants lists the supported resolutions in ascending order.
// Only variants with Height <= original video height are generated.
var hlsVariants = []hlsVariant{
	{"240p", 240, "400k", "64k", 464000},
	{"360p", 360, "800k", "96k", 896000},
	{"480p", 480, "1400k", "128k", 1528000},
	{"720p", 720, "2800k", "128k", 2928000},
	{"1080p", 1080, "5000k", "192k", 5192000},
}

// HLSOptions controls adaptive HLS generation behavior.
type HLSOptions struct {
	SingleCommand bool
	Fallback      bool
	// VideoEncoder is VideoEncoderCPU or VideoEncoderNVENC (empty defaults to CPU).
	VideoEncoder string
	NVENCPreset  string
}

// SegmentForStreaming generates adaptive HLS segments for multiple resolutions.
// It defaults to single-command generation with sequential fallback.
func SegmentForStreaming(ctx context.Context, inputPath, outputDir string) error {
	return SegmentForStreamingWithOptions(ctx, inputPath, outputDir, HLSOptions{
		SingleCommand: true,
		Fallback:      true,
		VideoEncoder:  VideoEncoderCPU,
	})
}

// SegmentForStreamingWithOptions generates adaptive HLS segments with runtime options.
func SegmentForStreamingWithOptions(ctx context.Context, inputPath, outputDir string, opts HLSOptions) error {
	encoder := strings.ToLower(strings.TrimSpace(opts.VideoEncoder))
	if encoder == "" {
		encoder = VideoEncoderCPU
	}
	preset := NormalizeNVENCPreset(opts.NVENCPreset)

	err := segmentForStreamingBody(ctx, inputPath, outputDir, opts, encoder, preset)
	if err != nil && encoder == VideoEncoderNVENC {
		log.Warn().Err(err).Msg("HLS with NVENC failed, retrying with CPU (libx264)")
		return segmentForStreamingBody(ctx, inputPath, outputDir, opts, VideoEncoderCPU, preset)
	}
	return err
}

func segmentForStreamingBody(ctx context.Context, inputPath, outputDir string, opts HLSOptions, encoder, nvencPreset string) error {
	if err := os.MkdirAll(outputDir, 0755); err != nil {
		return fmt.Errorf("failed to create directory: %w", err)
	}

	sourceHeight := probeSourceHeight(ctx, inputPath)

	var selected []hlsVariant
	for _, v := range hlsVariants {
		if sourceHeight == 0 || v.Height <= sourceHeight {
			selected = append(selected, v)
		}
	}
	if len(selected) == 0 {
		selected = hlsVariants[:1]
	}

	if opts.SingleCommand {
		err := segmentForStreamingSingleCommand(ctx, inputPath, outputDir, selected, encoder, nvencPreset)
		if err == nil {
			return nil
		}
		if !opts.Fallback {
			return err
		}
		log.Warn().Err(err).Msg("Single-command HLS failed, falling back to sequential mode")
	}

	return segmentForStreamingSequential(ctx, inputPath, outputDir, selected, encoder, nvencPreset)
}

func segmentForStreamingSequential(ctx context.Context, inputPath, outputDir string, selected []hlsVariant, encoder, nvencPreset string) error {
	for _, v := range selected {
		varDir := filepath.Join(outputDir, v.Name)
		if err := os.MkdirAll(varDir, 0755); err != nil {
			return fmt.Errorf("failed to create directory %s: %w", v.Name, err)
		}
		if err := transcodeHLSVariant(ctx, inputPath, varDir, v, encoder, nvencPreset); err != nil {
			return err
		}
	}

	return writeMasterPlaylist(outputDir, selected)
}

func segmentForStreamingSingleCommand(ctx context.Context, inputPath, outputDir string, selected []hlsVariant, encoder, nvencPreset string) error {
	for _, v := range selected {
		if err := os.MkdirAll(filepath.Join(outputDir, v.Name), 0755); err != nil {
			return fmt.Errorf("failed to create directory %s: %w", v.Name, err)
		}
	}

	hasAudio := probeSourceHasAudio(ctx, inputPath)
	args := make([]string, 0, 128)
	args = append(args, "-i", inputPath)

	splitOutputs := make([]string, 0, len(selected))
	filterParts := make([]string, 0, len(selected)+1)
	for i, v := range selected {
		label := fmt.Sprintf("v%d", i)
		scaled := fmt.Sprintf("v%dout", i)
		splitOutputs = append(splitOutputs, "["+label+"]")
		filterParts = append(filterParts, fmt.Sprintf("[%s]scale=-2:%d[%s]", label, v.Height, scaled))
	}
	filterComplex := fmt.Sprintf("[0:v]split=%d%s;%s", len(selected), strings.Join(splitOutputs, ""), strings.Join(filterParts, ";"))
	args = append(args, "-filter_complex", filterComplex)

	varStreamParts := make([]string, 0, len(selected))
	for i, v := range selected {
		args = append(args, "-map", fmt.Sprintf("[v%dout]", i))
		if hasAudio {
			args = append(args, "-map", "0:a:0?")
		}

		args = appendHLSSingleCommandVideoArgs(args, i, v, encoder, nvencPreset)

		if hasAudio {
			args = append(args,
				"-c:a:"+strconv.Itoa(i), "aac",
				"-b:a:"+strconv.Itoa(i), v.AudioBitrate,
			)
			varStreamParts = append(varStreamParts, fmt.Sprintf("v:%d,a:%d,name:%s", i, i, v.Name))
		} else {
			varStreamParts = append(varStreamParts, fmt.Sprintf("v:%d,name:%s", i, v.Name))
		}
	}

	args = append(args,
		"-f", "hls",
		"-hls_time", "6",
		"-hls_list_size", "0",
		"-hls_flags", "independent_segments",
		"-master_pl_name", "master.m3u8",
		"-var_stream_map", strings.Join(varStreamParts, " "),
		"-hls_segment_filename", filepath.Join(outputDir, "%v", "seg_%03d.ts"),
		"-y",
		filepath.Join(outputDir, "%v", "playlist.m3u8"),
	)

	cmd := exec.CommandContext(ctx, "ffmpeg", args...)
	output, err := cmd.CombinedOutput()
	if err != nil {
		return fmt.Errorf("single-command segmentation failed: %w, output: %s", err, string(output))
	}
	return nil
}

func appendHLSSingleCommandVideoArgs(args []string, streamIdx int, v hlsVariant, encoder, nvencPreset string) []string {
	si := strconv.Itoa(streamIdx)
	switch encoder {
	case VideoEncoderNVENC:
		p := NormalizeNVENCPreset(nvencPreset)
		return append(args,
			"-c:v:"+si, "h264_nvenc",
			"-preset", p,
			"-tune", "hq",
			"-rc", "vbr",
			"-cq", "23",
			"-b:v:"+si, v.VideoBitrate,
		)
	default:
		return append(args,
			"-c:v:"+si, "libx264",
			"-preset", "fast",
			"-crf", "23",
			"-b:v:"+si, v.VideoBitrate,
		)
	}
}

func transcodeHLSVariant(ctx context.Context, inputPath, varDir string, v hlsVariant, encoder, nvencPreset string) error {
	if strings.ToLower(strings.TrimSpace(encoder)) == VideoEncoderNVENC {
		return transcodeHLSVariantNVENC(ctx, inputPath, varDir, v, nvencPreset)
	}
	return transcodeHLSVariantCPU(ctx, inputPath, varDir, v)
}

func transcodeHLSVariantCPU(ctx context.Context, inputPath, varDir string, v hlsVariant) error {
	playlistPath := filepath.Join(varDir, "playlist.m3u8")
	segmentPath := filepath.Join(varDir, "seg_%03d.ts")

	cmd := exec.CommandContext(ctx, "ffmpeg",
		"-i", inputPath,
		"-c:v", "libx264",
		"-preset", "fast",
		"-vf", fmt.Sprintf("scale=-2:%d", v.Height),
		"-b:v", v.VideoBitrate,
		"-c:a", "aac",
		"-b:a", v.AudioBitrate,
		"-f", "hls",
		"-hls_time", "6",
		"-hls_list_size", "0",
		"-hls_segment_filename", segmentPath,
		"-y",
		playlistPath,
	)

	output, err := cmd.CombinedOutput()
	if err != nil {
		return fmt.Errorf("segmentation failed %s: %w, output: %s", v.Name, err, string(output))
	}
	return nil
}

func transcodeHLSVariantNVENC(ctx context.Context, inputPath, varDir string, v hlsVariant, nvencPreset string) error {
	preset := NormalizeNVENCPreset(nvencPreset)
	playlistPath := filepath.Join(varDir, "playlist.m3u8")
	segmentPath := filepath.Join(varDir, "seg_%03d.ts")

	args := []string{
		"-hwaccel", "cuda",
		"-i", inputPath,
		"-c:v", "h264_nvenc",
		"-preset", preset,
		"-tune", "hq",
		"-rc", "vbr",
		"-cq", "23",
		"-vf", fmt.Sprintf("scale=-2:%d", v.Height),
		"-b:v", v.VideoBitrate,
		"-c:a", "aac",
		"-b:a", v.AudioBitrate,
		"-f", "hls",
		"-hls_time", "6",
		"-hls_list_size", "0",
		"-hls_segment_filename", segmentPath,
		"-y",
		playlistPath,
	}
	cmd := exec.CommandContext(ctx, "ffmpeg", args...)
	output, err := cmd.CombinedOutput()
	if err == nil {
		return nil
	}
	log.Warn().Err(err).Str("variant", v.Name).Msg("HLS variant NVENC with CUDA decode failed, retrying without hwaccel")

	args = []string{
		"-i", inputPath,
		"-c:v", "h264_nvenc",
		"-preset", preset,
		"-tune", "hq",
		"-rc", "vbr",
		"-cq", "23",
		"-vf", fmt.Sprintf("scale=-2:%d", v.Height),
		"-b:v", v.VideoBitrate,
		"-c:a", "aac",
		"-b:a", v.AudioBitrate,
		"-f", "hls",
		"-hls_time", "6",
		"-hls_list_size", "0",
		"-hls_segment_filename", segmentPath,
		"-y",
		playlistPath,
	}
	cmd = exec.CommandContext(ctx, "ffmpeg", args...)
	output, err = cmd.CombinedOutput()
	if err != nil {
		return fmt.Errorf("segmentation failed %s: %w, output: %s", v.Name, err, string(output))
	}
	return nil
}

func writeMasterPlaylist(outputDir string, variants []hlsVariant) error {
	var sb strings.Builder
	sb.WriteString("#EXTM3U\n#EXT-X-VERSION:3\n\n")
	for _, v := range variants {
		fmt.Fprintf(&sb, "#EXT-X-STREAM-INF:BANDWIDTH=%d\n%s/playlist.m3u8\n", v.Bandwidth, v.Name)
	}
	masterPath := filepath.Join(outputDir, "master.m3u8")
	return os.WriteFile(masterPath, []byte(sb.String()), 0644)
}

func probeSourceHeight(ctx context.Context, inputPath string) int {
	cmd := exec.CommandContext(ctx, "ffprobe",
		"-v", "quiet",
		"-select_streams", "v:0",
		"-show_entries", "stream=height",
		"-of", "csv=p=0",
		inputPath,
	)
	out, err := cmd.Output()
	if err != nil {
		return 0
	}
	h, _ := strconv.Atoi(strings.TrimSpace(string(out)))
	return h
}

func probeSourceHasAudio(ctx context.Context, inputPath string) bool {
	cmd := exec.CommandContext(ctx, "ffprobe",
		"-v", "error",
		"-select_streams", "a:0",
		"-show_entries", "stream=index",
		"-of", "csv=p=0",
		inputPath,
	)
	out, err := cmd.Output()
	if err != nil {
		return false
	}
	return strings.TrimSpace(string(out)) != ""
}
