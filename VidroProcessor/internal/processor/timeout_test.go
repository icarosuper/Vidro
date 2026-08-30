package processor

import (
	"testing"
	"time"
)

// The whole-job budget must never be smaller than the pipeline it wraps —
// that was the bug: a 5min processCtx around 13min of step timeouts.
func TestJobBudgetExceedsStepTimeouts(t *testing.T) {
	steps := stepTimeoutValidate + stepTimeoutAnalyze + stepTimeoutTranscode +
		stepTimeoutThumbnails + stepTimeoutAudio + stepTimeoutPreview + stepTimeoutStreaming

	for _, scale := range []float64{0, 0.5, 1, 2.5} {
		budget := JobBudget(scale)
		if budget <= scaleTimeout(steps, scale) {
			t.Errorf("scale %v: budget %v does not exceed step sum %v", scale, budget, scaleTimeout(steps, scale))
		}
	}
}

func TestTimeoutScale(t *testing.T) {
	if got := (Options{TimeoutScale: 2}).step(time.Minute); got != 2*time.Minute {
		t.Errorf("scale 2: got %v, want 2m", got)
	}
	// Zero value must behave as 1, not as "no timeout".
	if got := (Options{}).step(time.Minute); got != time.Minute {
		t.Errorf("zero scale: got %v, want 1m", got)
	}
	if got := JobBudget(2); got != 2*JobBudget(1) {
		t.Errorf("JobBudget(2) = %v, want 2x %v", got, JobBudget(1))
	}
}
