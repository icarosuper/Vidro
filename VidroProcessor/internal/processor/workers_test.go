package processor

import "testing"

// The reason the default exists: workers times the FFmpeg processes one job can spawn
// must not exceed the cores. NumCPU workers used to break this by exactly the number of
// parallel post-transcode steps.
func TestDefaultWorkerCountDoesNotOversubscribeCores(t *testing.T) {
	for _, numCPU := range []int{1, 2, 4, 8, 16, 64} {
		for _, maxParallelSteps := range []int{1, 2, 4, 8} {
			workers := DefaultWorkerCount(numCPU, true, maxParallelSteps)
			concurrentFFmpeg := workers * clampParallelSteps(maxParallelSteps)

			if workers < 1 {
				t.Errorf("cpu %d, steps %d: got %d workers, want at least 1", numCPU, maxParallelSteps, workers)
			}
			// A single worker on a small host is allowed to exceed the cores: refusing to
			// run at all is worse than oversubscribing one job.
			if workers > 1 && concurrentFFmpeg > numCPU {
				t.Errorf("cpu %d, steps %d: %d workers x %d steps = %d concurrent FFmpeg, want <= %d",
					numCPU, maxParallelSteps, workers, clampParallelSteps(maxParallelSteps), concurrentFFmpeg, numCPU)
			}
		}
	}
}

// Sequential steps mean one FFmpeg process per job, so the old one-per-core default is
// right again. The knob that caused the contention is the knob that undoes it.
func TestDefaultWorkerCountSequentialStepsUsesEveryCore(t *testing.T) {
	if got := DefaultWorkerCount(8, false, 4); got != 8 {
		t.Errorf("sequential steps on 8 cores: got %d workers, want 8", got)
	}
	if got := DefaultWorkerCount(8, true, 1); got != 8 {
		t.Errorf("one parallel step on 8 cores: got %d workers, want 8", got)
	}
}

func TestDefaultWorkerCountNeverZero(t *testing.T) {
	// Fewer cores than parallel steps must still start a worker.
	if got := DefaultWorkerCount(2, true, 4); got != 1 {
		t.Errorf("2 cores, 4 parallel steps: got %d workers, want 1", got)
	}
	// A host that reports nothing must not silently start zero workers.
	if got := DefaultWorkerCount(0, true, 4); got != 1 {
		t.Errorf("0 cores: got %d workers, want 1", got)
	}
}

// clampParallelSteps is shared with runNonCriticalStepsParallel on purpose: a test that
// copies the bound would not catch the two drifting apart.
func TestClampParallelSteps(t *testing.T) {
	cases := map[int]int{-1: 1, 0: 1, 1: 1, 4: 4, 5: 4, 100: 4}
	for given, want := range cases {
		if got := clampParallelSteps(given); got != want {
			t.Errorf("clampParallelSteps(%d) = %d, want %d", given, got, want)
		}
	}
}
