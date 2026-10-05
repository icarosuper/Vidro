package processor

import (
	"context"
	"os"
	"os/exec"
	"path/filepath"
	"strconv"
	"sync"
	"testing"

	processor_steps "video-processor/internal/processor/processor-steps"
)

// Benchmarks over the real FFmpeg pipeline (P-PERF6). They only run with -bench, and skip
// when ffmpeg/ffprobe are missing. Run: go test ./internal/processor -run '^$' -bench . -benchtime 3x -count 3
//
// The clip is a synthetic 10s 720p testsrc+sine, generated once per run: it is not a
// 500 MB production video, so absolute numbers say little — read the ratios.

var (
	clipOnce sync.Once
	clipPath string
	clipErr  error
)

func benchClip(b *testing.B) string {
	b.Helper()
	for _, bin := range []string{"ffmpeg", "ffprobe"} {
		if _, err := exec.LookPath(bin); err != nil {
			b.Skipf("%s is not available", bin)
		}
	}
	clipOnce.Do(func() {
		dir, err := os.MkdirTemp("", "vidro-bench-")
		if err != nil {
			clipErr = err
			return
		}
		clipPath = filepath.Join(dir, "clip.mp4")
		clipErr = exec.Command("ffmpeg", "-f", "lavfi", "-i", "testsrc=duration=10:size=1280x720:rate=30",
			"-f", "lavfi", "-i", "sine=frequency=1000:duration=10",
			"-pix_fmt", "yuv420p", "-c:v", "libx264", "-c:a", "aac", "-y", clipPath).Run()
	})
	if clipErr != nil {
		b.Fatalf("generating clip: %v", clipErr)
	}
	return clipPath
}

// BenchmarkTranscodeVideo is variable (a): the critical transcode step alone.
func BenchmarkTranscodeVideo(b *testing.B) {
	input := benchClip(b)
	dir := b.TempDir()
	for i := 0; i < b.N; i++ {
		out := filepath.Join(dir, "out_"+strconv.Itoa(i)+".mp4")
		if err := processor_steps.TranscodeVideo(context.Background(), input, out, processor_steps.VideoEncoderCPU, ""); err != nil {
			b.Fatal(err)
		}
	}
}

// BenchmarkNonCriticalSteps is variable (b): steps 4-7 on an already transcoded file,
// sequential vs the parallel orchestrator at MAX_PARALLEL_POST_TRANSCODE_STEPS = 1, 2, 4.
func BenchmarkNonCriticalSteps(b *testing.B) {
	input := benchClip(b)
	transcoded := filepath.Join(b.TempDir(), "transcoded.mp4")
	if err := processor_steps.TranscodeVideo(context.Background(), input, transcoded, processor_steps.VideoEncoderCPU, ""); err != nil {
		b.Fatal(err)
	}

	run := func(b *testing.B, orchestrate func(context.Context, []nonCriticalStep)) {
		for i := 0; i < b.N; i++ {
			result := &ProcessingResult{}
			steps := nonCriticalSteps(input, transcoded, b.TempDir(), result, DefaultOptions())
			orchestrate(context.Background(), steps)
			if result.ThumbnailsDir == "" || result.AudioPath == "" || result.PreviewPath == "" || result.StreamingDir == "" {
				b.Fatalf("a step failed: %+v", result)
			}
		}
	}

	b.Run("sequential", func(b *testing.B) { run(b, runNonCriticalStepsSequential) })
	for _, maxParallel := range []int{1, 2, 4} {
		b.Run("parallel-"+strconv.Itoa(maxParallel), func(b *testing.B) {
			run(b, func(ctx context.Context, steps []nonCriticalStep) {
				runNonCriticalStepsParallel(ctx, steps, maxParallel)
			})
		})
	}
}

// BenchmarkProcessVideo is variable (c): the whole pipeline with every ENABLE_* flag on
// vs steps 4-7 skipped (what P-OPT1 enables), with the default parallel orchestrator.
func BenchmarkProcessVideo(b *testing.B) {
	input := benchClip(b)
	cases := map[string]func(*Options){
		"all-steps": func(*Options) {},
		"no-optionals": func(o *Options) {
			o.SkipThumbnails, o.SkipAudio, o.SkipPreview, o.SkipStreaming = true, true, true, true
		},
	}
	for name, mutate := range cases {
		b.Run(name, func(b *testing.B) {
			for i := 0; i < b.N; i++ {
				opts := DefaultOptions()
				mutate(&opts)
				dir := b.TempDir()
				if _, err := ProcessVideo(context.Background(), input, filepath.Join(dir, "out.mp4"), opts); err != nil {
					b.Fatal(err)
				}
			}
		})
	}
}
