package processor

import (
	"context"
	"errors"
	"fmt"
	"os"
	"os/exec"
	"strings"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	"github.com/prometheus/client_golang/prometheus/testutil"

	"video-processor/metrics"
)

// The orchestrator's job is a policy, not a transformation: which step may fail without
// failing the job, how long each one gets, and what a failure does to its siblings.
// These tests exercise that policy with fake steps — no FFmpeg involved.

var errStepFailed = errors.New("step failed")

// fakeStep builds a step whose run function is fn and whose onSuccess appends its name
// to succeeded. Mirrors what the real steps do: write into the result only on success.
func fakeStep(name string, timeout time.Duration, fn func(context.Context) error, mu *sync.Mutex, started, succeeded *[]string) nonCriticalStep {
	return nonCriticalStep{
		name:     name,
		startMsg: "start " + name,
		failMsg:  "fail " + name,
		timeout:  timeout,
		run: func(stepCtx context.Context) error {
			mu.Lock()
			*started = append(*started, name)
			mu.Unlock()
			return fn(stepCtx)
		},
		onSuccess: func() {
			mu.Lock()
			*succeeded = append(*succeeded, name)
			mu.Unlock()
		},
	}
}

func succeeds(context.Context) error { return nil }
func fails(context.Context) error    { return errStepFailed }

// The four post-transcode steps are the contract of the pipeline: deleting one, renaming
// one or reordering them shows up here. nonCriticalStepCount is what clamps
// MAX_PARALLEL_POST_TRANSCODE_STEPS, so the count has to match the list.
func TestNonCriticalSteps_AreTheFourPostTranscodeSteps(t *testing.T) {
	result := &ProcessingResult{}
	steps := nonCriticalSteps("input.mp4", "output.mp4", t.TempDir(), result, DefaultOptions())

	wantNames := []string{"thumbnails", "audio", "preview", "streaming"}
	if len(steps) != len(wantNames) {
		t.Fatalf("expected %d steps, got %d", len(wantNames), len(steps))
	}
	if len(steps) != nonCriticalStepCount {
		t.Fatalf("nonCriticalStepCount is %d but there are %d steps", nonCriticalStepCount, len(steps))
	}
	for i, want := range wantNames {
		if steps[i].name != want {
			t.Errorf("step %d: expected %q, got %q", i, want, steps[i].name)
		}
	}
}

// Every step timeout must go through opts.step, otherwise PROCESSING_TIMEOUT_SCALE
// silently stops applying to that step.
func TestNonCriticalSteps_TimeoutsFollowTheScale(t *testing.T) {
	tempDir := t.TempDir()
	result := &ProcessingResult{}

	unscaled := nonCriticalSteps("input.mp4", "output.mp4", tempDir, result, Options{TimeoutScale: 1})
	scaled := nonCriticalSteps("input.mp4", "output.mp4", tempDir, result, Options{TimeoutScale: 3})

	for i := range unscaled {
		want := 3 * unscaled[i].timeout
		if scaled[i].timeout != want {
			t.Errorf("step %q: scale 3 gave %v, want %v", unscaled[i].name, scaled[i].timeout, want)
		}
	}
}

// A non-critical step that fails is logged and skipped: the following steps still run,
// and only the ones that succeeded write their artifact into the result.
func TestRunNonCriticalStepsSequential_FailingStepDoesNotStopTheRest(t *testing.T) {
	var mu sync.Mutex
	var started, succeeded []string

	steps := []nonCriticalStep{
		fakeStep("first", time.Minute, succeeds, &mu, &started, &succeeded),
		fakeStep("second", time.Minute, fails, &mu, &started, &succeeded),
		fakeStep("third", time.Minute, succeeds, &mu, &started, &succeeded),
	}

	runNonCriticalStepsSequential(context.Background(), steps)

	if got := strings.Join(started, ","); got != "first,second,third" {
		t.Errorf("every step should run in order, got: %s", got)
	}
	if got := strings.Join(succeeded, ","); got != "first,third" {
		t.Errorf("only successful steps record their artifact, got: %s", got)
	}
}

// The reason this orchestrator is a WaitGroup and not an errgroup: a step that fails must
// not cancel its siblings. With an errgroup, a thumbnail glitch would kill the HLS output.
func TestRunNonCriticalStepsParallel_FailingStepDoesNotCancelSiblings(t *testing.T) {
	var mu sync.Mutex
	var started, succeeded []string

	// Fails only after every sibling has started, so a cancellation would be observable.
	allStarted := make(chan struct{})
	var startedCount atomic.Int32
	waitThenCheckContext := func(stepCtx context.Context) error {
		if startedCount.Add(1) == 3 {
			close(allStarted)
		}
		<-allStarted
		return stepCtx.Err()
	}

	steps := []nonCriticalStep{
		fakeStep("failing", time.Minute, fails, &mu, &started, &succeeded),
		fakeStep("sibling-a", time.Minute, waitThenCheckContext, &mu, &started, &succeeded),
		fakeStep("sibling-b", time.Minute, waitThenCheckContext, &mu, &started, &succeeded),
	}
	// The failing step counts as started too, so the barrier needs all three.
	steps[0].run = func(stepCtx context.Context) error {
		mu.Lock()
		started = append(started, "failing")
		mu.Unlock()
		if startedCount.Add(1) == 3 {
			close(allStarted)
		}
		return errStepFailed
	}

	runNonCriticalStepsParallel(context.Background(), steps, nonCriticalStepCount)

	if len(started) != 3 {
		t.Errorf("every step should run, got: %v", started)
	}
	mu.Lock()
	defer mu.Unlock()
	if len(succeeded) != 2 {
		t.Errorf("both siblings should survive the failure, succeeded: %v", succeeded)
	}
}

// The per-job ceiling from P-PERF4: at most maxParallel FFmpeg processes at a time.
// Without it, one job alone can start four.
func TestRunNonCriticalStepsParallel_RespectsMaxParallel(t *testing.T) {
	const maxParallel = 2

	var running, peak atomic.Int32
	var mu sync.Mutex
	var started, succeeded []string

	observeConcurrency := func(context.Context) error {
		current := running.Add(1)
		for {
			recorded := peak.Load()
			if current <= recorded || peak.CompareAndSwap(recorded, current) {
				break
			}
		}
		time.Sleep(20 * time.Millisecond)
		running.Add(-1)
		return nil
	}

	steps := make([]nonCriticalStep, 0, nonCriticalStepCount)
	for _, name := range []string{"thumbnails", "audio", "preview", "streaming"} {
		steps = append(steps, fakeStep(name, time.Minute, observeConcurrency, &mu, &started, &succeeded))
	}

	runNonCriticalStepsParallel(context.Background(), steps, maxParallel)

	if peak.Load() > maxParallel {
		t.Errorf("ran %d steps at once, ceiling is %d", peak.Load(), maxParallel)
	}
	if peak.Load() < maxParallel {
		t.Errorf("peak concurrency was %d, so the steps did not run in parallel at all", peak.Load())
	}
	if len(succeeded) != nonCriticalStepCount {
		t.Errorf("every step should still complete, succeeded: %v", succeeded)
	}
}

func TestRunStep_ReturnsTheStepError(t *testing.T) {
	err := runStep(context.Background(), "validate", time.Minute, fails)

	if !errors.Is(err, errStepFailed) {
		t.Fatalf("expected the step error back, got: %v", err)
	}
}

// The per-step timeout is what stops a hung FFmpeg from eating the whole job budget.
func TestRunStep_AppliesTheStepTimeout(t *testing.T) {
	var deadlineWasSet bool

	start := time.Now()
	err := runStep(context.Background(), "transcode", 30*time.Millisecond, func(stepCtx context.Context) error {
		_, deadlineWasSet = stepCtx.Deadline()
		<-stepCtx.Done()
		return stepCtx.Err()
	})
	elapsed := time.Since(start)

	if !deadlineWasSet {
		t.Error("the step context should carry a deadline")
	}
	if !errors.Is(err, context.DeadlineExceeded) {
		t.Errorf("expected DeadlineExceeded, got: %v", err)
	}
	if elapsed > time.Second {
		t.Errorf("the step ran for %v — the timeout did not fire", elapsed)
	}
}

// Shutdown and the whole-job budget both work by cancelling the parent context.
func TestRunStep_ParentCancellationReachesTheStep(t *testing.T) {
	ctx, cancel := context.WithCancel(context.Background())
	cancel()

	err := runStep(ctx, "streaming", time.Minute, func(stepCtx context.Context) error {
		return stepCtx.Err()
	})

	if !errors.Is(err, context.Canceled) {
		t.Fatalf("expected Canceled, got: %v", err)
	}
}

// P-PERF4 guard rail: per-step duration stays measurable after the pipeline was
// parallelized. It is what P-PERF6 will read to finally measure the optimizations.
func TestRunStep_RecordsTheStepDuration(t *testing.T) {
	// A fresh label every run: the series a previous run left behind would already be
	// counted, and the assertion has to hold under `go test -count=3` too.
	stepName := fmt.Sprintf("metrics-probe-%d", time.Now().UnixNano())

	before := testutil.CollectAndCount(metrics.ProcessingStepDuration)
	runStep(context.Background(), stepName, time.Minute, succeeds)
	after := testutil.CollectAndCount(metrics.ProcessingStepDuration)

	if after <= before {
		t.Errorf("no series recorded for step %q: %d before, %d after", stepName, before, after)
	}
}

// Steps 1 and 3 are critical: when validation rejects the file the job stops there, and
// no later step gets to run. This one shells out to ffprobe, so it skips without FFmpeg.
func TestProcessVideo_InvalidInputFailsAtValidation(t *testing.T) {
	if _, err := exec.LookPath("ffprobe"); err != nil {
		t.Skip("ffprobe is not available - skipping test")
	}

	tempDir := t.TempDir()
	invalidPath := tempDir + "/invalid.mp4"
	if err := os.WriteFile(invalidPath, []byte("not a valid video"), 0o644); err != nil {
		t.Fatalf("failed to write the invalid file: %v", err)
	}

	result, err := ProcessVideo(context.Background(), invalidPath, tempDir+"/output.mp4", DefaultOptions())

	if err == nil {
		t.Fatal("ProcessVideo should fail on an invalid input")
	}
	if !strings.Contains(err.Error(), "validation failed") {
		t.Errorf("expected the failure to come from validation, got: %v", err)
	}
	if result == nil {
		t.Fatal("the result should come back even on failure, the caller cleans up TempDir")
	}
	if result.ThumbnailsDir != "" || result.AudioPath != "" || result.PreviewPath != "" || result.StreamingDir != "" {
		t.Errorf("no step after validation should have run, got: %+v", result)
	}
}
