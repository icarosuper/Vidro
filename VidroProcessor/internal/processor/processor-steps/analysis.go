package processor_steps

import (
	"context"
	"encoding/json"
	"fmt"
	"os/exec"
	"strconv"
	"strings"

	"github.com/rs/zerolog/log"
)

// VideoMetadata contains metadata extracted from the video.
type VideoMetadata struct {
	Duration   float64 `json:"duration"`
	Width      int     `json:"width"`
	Height     int     `json:"height"`
	VideoCodec string  `json:"video_codec"`
	AudioCodec string  `json:"audio_codec"`
	FPS        float64 `json:"fps"`
	Bitrate    int64   `json:"bitrate"`
	Size       int64   `json:"size"`
}

// AnalyzeContent extracts metadata and technical information from the video.
func AnalyzeContent(ctx context.Context, inputPath string) (*VideoMetadata, error) {
	cmd := exec.CommandContext(ctx, "ffprobe",
		"-v", "quiet",
		"-print_format", "json",
		"-show_format",
		"-show_streams",
		inputPath,
	)

	output, err := cmd.Output()
	if err != nil {
		return nil, fmt.Errorf("analysis failed: %w", err)
	}

	var probeData struct {
		Format struct {
			Duration string `json:"duration"`
			Size     string `json:"size"`
			BitRate  string `json:"bit_rate"`
		} `json:"format"`
		Streams []struct {
			CodecType  string `json:"codec_type"`
			CodecName  string `json:"codec_name"`
			Width      int    `json:"width"`
			Height     int    `json:"height"`
			RFrameRate string `json:"r_frame_rate"`
		} `json:"streams"`
	}

	if err := json.Unmarshal(output, &probeData); err != nil {
		return nil, fmt.Errorf("failed to parse JSON: %w", err)
	}

	metadata := &VideoMetadata{}

	for _, stream := range probeData.Streams {
		if stream.CodecType == "video" {
			metadata.Width = stream.Width
			metadata.Height = stream.Height
			metadata.VideoCodec = stream.CodecName

			if parts := strings.Split(stream.RFrameRate, "/"); len(parts) == 2 {
				numerator, _ := strconv.ParseFloat(parts[0], 64)
				denominator, _ := strconv.ParseFloat(parts[1], 64)
				if denominator != 0 {
					metadata.FPS = numerator / denominator
				}
			}
		} else if stream.CodecType == "audio" {
			metadata.AudioCodec = stream.CodecName
		}
	}

	metadata.Duration, _ = strconv.ParseFloat(probeData.Format.Duration, 64)
	metadata.Size, _ = strconv.ParseInt(probeData.Format.Size, 10, 64)
	metadata.Bitrate, _ = strconv.ParseInt(probeData.Format.BitRate, 10, 64)

	log.Info().
		Float64("duration", metadata.Duration).
		Int("width", metadata.Width).
		Int("height", metadata.Height).
		Str("videoCodec", metadata.VideoCodec).
		Str("audioCodec", metadata.AudioCodec).
		Float64("fps", metadata.FPS).
		Int64("bitrate", metadata.Bitrate).
		Int64("size", metadata.Size).
		Msg("Video metadata extracted")

	return metadata, nil
}
