package queue

import (
	"context"
	"encoding/json"
	"errors"
	"strings"
	"testing"
	"time"

	"github.com/alicebob/miniredis/v2"
	"github.com/redis/go-redis/v9"

	"video-processor/config"
)

var errBoom = errors.New("boom")

// setupRedis points the package globals at an in-process Redis. No Docker, so
// these tests always run — unlike everything under test/integration.
func setupRedis(t *testing.T) *miniredis.Miniredis {
	t.Helper()
	mr := miniredis.RunT(t)
	client = redis.NewClient(&redis.Options{Addr: mr.Addr()})
	cfg = &config.Config{
		RedisHost:               mr.Addr(),
		ProcessingRequestQueue:  "videos",
		ProcessingFinishedQueue: "videos:done",
	}
	t.Cleanup(func() { client.Close() })
	return mr
}

func listOf(t *testing.T, key string) []string {
	t.Helper()
	items, err := client.LRange(context.Background(), key, 0, -1).Result()
	if err != nil {
		t.Fatalf("LRange %s: %v", key, err)
	}
	return items
}

// --- retry budget -----------------------------------------------------------

func TestSetJobFailed_IncrementsRetryCountAndPersists(t *testing.T) {
	setupRedis(t)

	if err := PublishJob(t.Context(), "vid", "", ""); err != nil {
		t.Fatalf("PublishJob: %v", err)
	}

	for attempt := 1; attempt <= 4; attempt++ {
		state, err := SetJobFailed(t.Context(), "vid", errBoom)
		if err != nil {
			t.Fatalf("SetJobFailed: %v", err)
		}
		if state.RetryCount != attempt {
			t.Fatalf("attempt %d: RetryCount = %d, want %d", attempt, state.RetryCount, attempt)
		}
		if state.Status != JobStatusFailed {
			t.Fatalf("attempt %d: Status = %q, want %q", attempt, state.Status, JobStatusFailed)
		}
		if state.Error != errBoom.Error() {
			t.Fatalf("attempt %d: Error = %q, want %q", attempt, state.Error, errBoom.Error())
		}

		// The counter has to survive the round-trip, not just live in the return value.
		stored, err := GetJobState(t.Context(), "vid")
		if err != nil {
			t.Fatalf("GetJobState: %v", err)
		}
		if stored.RetryCount != attempt {
			t.Fatalf("attempt %d: stored RetryCount = %d, want %d", attempt, stored.RetryCount, attempt)
		}
	}
}

func TestSetJobFailed_WithoutExistingState(t *testing.T) {
	setupRedis(t)

	state, err := SetJobFailed(t.Context(), "ghost", errBoom)
	if err != nil {
		t.Fatalf("SetJobFailed: %v", err)
	}
	if state.RetryCount != 1 {
		t.Fatalf("RetryCount = %d, want 1", state.RetryCount)
	}
}

// TestShouldRetry_Boundary locks the meaning of MaxJobRetries: the initial
// attempt plus MaxJobRetries retries. An off-by-one here either sends a healthy
// job to the DLQ or reprocesses a poisoned one forever.
func TestShouldRetry_Boundary(t *testing.T) {
	for _, tc := range []struct {
		retryCount int
		want       bool
	}{
		{1, true},
		{2, true},
		{3, true},
		{4, false},
		{5, false},
	} {
		state := JobState{RetryCount: tc.retryCount}
		if got := state.ShouldRetry(); got != tc.want {
			t.Errorf("RetryCount %d: ShouldRetry() = %v, want %v", tc.retryCount, got, tc.want)
		}
	}
}

// --- queue mechanics --------------------------------------------------------

func TestPublishJob_QueuesAndRecordsPending(t *testing.T) {
	setupRedis(t)

	if err := PublishJob(t.Context(), "vid", "http://api/hook", ""); err != nil {
		t.Fatalf("PublishJob: %v", err)
	}

	if got := listOf(t, cfg.ProcessingRequestQueue); len(got) != 1 || got[0] != "vid" {
		t.Fatalf("request queue = %v, want [vid]", got)
	}
	state, err := GetJobState(t.Context(), "vid")
	if err != nil {
		t.Fatalf("GetJobState: %v", err)
	}
	if state.Status != JobStatusPending {
		t.Fatalf("Status = %q, want %q", state.Status, JobStatusPending)
	}
	if state.CallbackURL != "http://api/hook" {
		t.Fatalf("CallbackURL = %q", state.CallbackURL)
	}
}

// TestConsumeMessage_MovesJobToProcessing is the in-flight guarantee: a job
// pulled off the queue must be parked in the processing list, or a worker
// crash between pop and completion loses it with nothing left to recover.
func TestConsumeMessage_MovesJobToProcessing(t *testing.T) {
	setupRedis(t)

	if err := PublishJob(t.Context(), "vid", "", ""); err != nil {
		t.Fatalf("PublishJob: %v", err)
	}

	msg, err := ConsumeMessage(context.Background())
	if err != nil {
		t.Fatalf("ConsumeMessage: %v", err)
	}
	if msg.VideoID != "vid" {
		t.Fatalf("VideoID = %q, want vid", msg.VideoID)
	}
	if got := listOf(t, cfg.ProcessingRequestQueue); len(got) != 0 {
		t.Fatalf("request queue = %v, want empty", got)
	}
	if got := listOf(t, processingQueueName()); len(got) != 1 || got[0] != "vid" {
		t.Fatalf("processing queue = %v, want [vid]", got)
	}
}

func TestAcknowledgeMessage_RemovesOneOccurrence(t *testing.T) {
	setupRedis(t)

	for range 2 {
		if err := client.LPush(context.Background(), processingQueueName(), "vid").Err(); err != nil {
			t.Fatalf("LPush: %v", err)
		}
	}

	if err := AcknowledgeMessage(t.Context(), "vid"); err != nil {
		t.Fatalf("AcknowledgeMessage: %v", err)
	}
	if got := listOf(t, processingQueueName()); len(got) != 1 {
		t.Fatalf("processing queue = %v, want one entry left", got)
	}
}

func TestRequeueJob_BackToRequestQueueAsPending(t *testing.T) {
	setupRedis(t)

	if _, err := SetJobFailed(t.Context(), "vid", errBoom); err != nil {
		t.Fatalf("SetJobFailed: %v", err)
	}
	if err := RequeueJob(t.Context(), "vid"); err != nil {
		t.Fatalf("RequeueJob: %v", err)
	}

	if got := listOf(t, cfg.ProcessingRequestQueue); len(got) != 1 || got[0] != "vid" {
		t.Fatalf("request queue = %v, want [vid]", got)
	}
	state, err := GetJobState(t.Context(), "vid")
	if err != nil {
		t.Fatalf("GetJobState: %v", err)
	}
	if state.Status != JobStatusPending {
		t.Fatalf("Status = %q, want %q", state.Status, JobStatusPending)
	}
	if state.RetryCount != 1 {
		t.Fatalf("RetryCount = %d, want 1 (requeue must not reset the budget)", state.RetryCount)
	}
}

func TestMoveToDLQ_LandsInDeadQueueOnly(t *testing.T) {
	setupRedis(t)

	if err := MoveToDLQ(t.Context(), "vid"); err != nil {
		t.Fatalf("MoveToDLQ: %v", err)
	}

	if got := listOf(t, deadLetterQueueName()); len(got) != 1 || got[0] != "vid" {
		t.Fatalf("dead letter queue = %v, want [vid]", got)
	}
	if got := listOf(t, cfg.ProcessingRequestQueue); len(got) != 0 {
		t.Fatalf("request queue = %v, want empty", got)
	}
}

func TestPublishSuccessMessage(t *testing.T) {
	setupRedis(t)

	if err := PublishSuccessMessage(t.Context(), "vid"); err != nil {
		t.Fatalf("PublishSuccessMessage: %v", err)
	}
	if got := listOf(t, cfg.ProcessingFinishedQueue); len(got) != 1 || got[0] != "vid" {
		t.Fatalf("finished queue = %v, want [vid]", got)
	}
}

// --- orphan recovery --------------------------------------------------------

// parkInProcessing puts a job in the processing list with a hand-written state,
// simulating a worker that picked it up ageAgo and then died.
func parkInProcessing(t *testing.T, videoID string, status JobStatus, retryCount int, ageAgo time.Duration) {
	t.Helper()
	if err := client.LPush(context.Background(), processingQueueName(), videoID).Err(); err != nil {
		t.Fatalf("LPush: %v", err)
	}
	if err := setJobState(t.Context(), videoID, JobState{Status: status, RetryCount: retryCount}); err != nil {
		t.Fatalf("setJobState: %v", err)
	}
	// setJobState stamps UpdatedAt with time.Now, so age it explicitly.
	state, err := GetJobState(t.Context(), videoID)
	if err != nil {
		t.Fatalf("GetJobState: %v", err)
	}
	state.UpdatedAt = time.Now().Add(-ageAgo).Unix()
	data, err := json.Marshal(state)
	if err != nil {
		t.Fatalf("marshal: %v", err)
	}
	if err := client.Set(context.Background(), jobKey(videoID), data, jobTTL).Err(); err != nil {
		t.Fatalf("Set: %v", err)
	}
}

func TestRecoverStuckJobs_RequeuesOrphan(t *testing.T) {
	setupRedis(t)
	parkInProcessing(t, "vid", JobStatusProcessing, 1, time.Hour)

	recoverStuckJobs(t.Context(), 30*time.Minute)

	if got := listOf(t, processingQueueName()); len(got) != 0 {
		t.Fatalf("processing queue = %v, want empty", got)
	}
	if got := listOf(t, cfg.ProcessingRequestQueue); len(got) != 1 || got[0] != "vid" {
		t.Fatalf("request queue = %v, want [vid]", got)
	}
	state, err := GetJobState(t.Context(), "vid")
	if err != nil {
		t.Fatalf("GetJobState: %v", err)
	}
	if state.RetryCount != 2 {
		t.Fatalf("RetryCount = %d, want 2", state.RetryCount)
	}
	if state.Status != JobStatusPending {
		t.Fatalf("Status = %q, want %q", state.Status, JobStatusPending)
	}
}

// TestRecoverStuckJobs_ExhaustedOrphanGoesToDLQ covers the mirror of the
// worker's failure path: a video that hangs the worker orphans every time, so
// without a budget check recovery would re-queue it forever.
func TestRecoverStuckJobs_ExhaustedOrphanGoesToDLQ(t *testing.T) {
	setupRedis(t)
	parkInProcessing(t, "vid", JobStatusProcessing, MaxJobRetries, time.Hour)

	recoverStuckJobs(t.Context(), 30*time.Minute)

	if got := listOf(t, deadLetterQueueName()); len(got) != 1 || got[0] != "vid" {
		t.Fatalf("dead letter queue = %v, want [vid]", got)
	}
	if got := listOf(t, cfg.ProcessingRequestQueue); len(got) != 0 {
		t.Fatalf("request queue = %v, want empty", got)
	}
	if got := listOf(t, processingQueueName()); len(got) != 0 {
		t.Fatalf("processing queue = %v, want empty", got)
	}
	state, err := GetJobState(t.Context(), "vid")
	if err != nil {
		t.Fatalf("GetJobState: %v", err)
	}
	if state.Status != JobStatusFailed {
		t.Fatalf("Status = %q, want %q", state.Status, JobStatusFailed)
	}
}

func TestRecoverStuckJobs_LeavesHealthyAndUnknownJobsAlone(t *testing.T) {
	for _, tc := range []struct {
		name   string
		setup  func(t *testing.T)
		reason string
	}{
		{
			name:   "job still within the timeout",
			setup:  func(t *testing.T) { parkInProcessing(t, "vid", JobStatusProcessing, 0, time.Minute) },
			reason: "a healthy in-flight job must not be reprocessed",
		},
		{
			name:   "job already finished",
			setup:  func(t *testing.T) { parkInProcessing(t, "vid", JobStatusDone, 0, time.Hour) },
			reason: "a done job lingering in the list must not be reprocessed",
		},
		{
			name: "job with no state in Redis",
			setup: func(t *testing.T) {
				if err := client.LPush(context.Background(), processingQueueName(), "vid").Err(); err != nil {
					t.Fatalf("LPush: %v", err)
				}
			},
			reason: "an expired state must neither drop nor duplicate the job",
		},
	} {
		t.Run(tc.name, func(t *testing.T) {
			setupRedis(t)
			tc.setup(t)

			recoverStuckJobs(t.Context(), 30*time.Minute)

			if got := listOf(t, processingQueueName()); len(got) != 1 {
				t.Fatalf("processing queue = %v, want [vid]: %s", got, tc.reason)
			}
			if got := listOf(t, cfg.ProcessingRequestQueue); len(got) != 0 {
				t.Fatalf("request queue = %v, want empty: %s", got, tc.reason)
			}
			if got := listOf(t, deadLetterQueueName()); len(got) != 0 {
				t.Fatalf("dead letter queue = %v, want empty: %s", got, tc.reason)
			}
		})
	}
}

// --- cancellation reaches Redis ---------------------------------------------

// The whole point of threading ctx through this package: when the job budget blows or the
// worker gets SIGTERM, the cancellation has to reach Redis instead of the call running to
// completion against context.Background(). One canceled context, every public entry point.
func TestQueueOperations_StopOnCanceledContext(t *testing.T) {
	setupRedis(t)

	canceled, cancel := context.WithCancel(t.Context())
	cancel()

	operations := map[string]func() error{
		"PublishJob":            func() error { return PublishJob(canceled, "vid", "", "") },
		"SetJobProcessing":      func() error { return SetJobProcessing(canceled, "vid") },
		"SetJobDone":            func() error { return SetJobDone(canceled, "vid", JobArtifacts{}, nil) },
		"SetJobFailed":          func() error { _, err := SetJobFailed(canceled, "vid", errBoom); return err },
		"RequeueJob":            func() error { return RequeueJob(canceled, "vid") },
		"MoveToDLQ":             func() error { return MoveToDLQ(canceled, "vid") },
		"GetJobState":           func() error { _, err := GetJobState(canceled, "vid"); return err },
		"AcknowledgeMessage":    func() error { return AcknowledgeMessage(canceled, "vid") },
		"PublishSuccessMessage": func() error { return PublishSuccessMessage(canceled, "vid") },
		"GetQueueSize":          func() error { _, err := GetQueueSize(canceled); return err },
		"HealthCheck":           func() error { return HealthCheck(canceled) },
	}

	for name, operation := range operations {
		t.Run(name, func(t *testing.T) {
			err := operation()
			if !errors.Is(err, context.Canceled) {
				t.Errorf("expected context.Canceled, got %v", err)
			}
		})
	}
}

// The counterpart of the test above: recovery reads the queue through the ticker's context,
// so a shutdown stops it mid-sweep instead of finishing a scan nobody is waiting for.
func TestRecoverStuckJobs_StopsOnCanceledContext(t *testing.T) {
	setupRedis(t)

	videoID := "vid"
	parkInProcessing(t, videoID, JobStatusProcessing, 0, time.Hour)

	canceled, cancel := context.WithCancel(t.Context())
	cancel()

	recoverStuckJobs(canceled, 30*time.Minute)

	// Nothing moved: the LRange that starts the sweep failed on the canceled context.
	if got := listOf(t, processingQueueName()); len(got) != 1 {
		t.Errorf("expected the job untouched in the processing queue, got %v", got)
	}
}

// --- correlation ID ---------------------------------------------------------

// The ID only earns its keep if it survives to the end of the job: the webhook reads it from
// the *final* state, which every status write rebuilds from the stored one.
func TestJobState_CorrelationIDSurvivesEveryStatusWrite(t *testing.T) {
	setupRedis(t)

	if err := PublishJob(t.Context(), "vid", "http://api/hook", "corr-42"); err != nil {
		t.Fatalf("PublishJob: %v", err)
	}

	writes := []struct {
		name string
		run  func() error
	}{
		{"SetJobProcessing", func() error { return SetJobProcessing(t.Context(), "vid") }},
		{"SetJobFailed", func() error { _, err := SetJobFailed(t.Context(), "vid", errBoom); return err }},
		{"RequeueJob", func() error { return RequeueJob(t.Context(), "vid") }},
		{"SetJobDone", func() error { return SetJobDone(t.Context(), "vid", JobArtifacts{}, nil) }},
	}

	for _, write := range writes {
		if err := write.run(); err != nil {
			t.Fatalf("%s: %v", write.name, err)
		}
		state, err := GetJobState(t.Context(), "vid")
		if err != nil {
			t.Fatalf("GetJobState after %s: %v", write.name, err)
		}
		if state.CorrelationID != "corr-42" {
			t.Fatalf("after %s: CorrelationID = %q, want %q", write.name, state.CorrelationID, "corr-42")
		}
	}
}

func TestPublishJob_WithoutCorrelationIDOmitsTheField(t *testing.T) {
	setupRedis(t)

	if err := PublishJob(t.Context(), "vid", "", ""); err != nil {
		t.Fatalf("PublishJob: %v", err)
	}

	raw, err := client.Get(t.Context(), jobKey("vid")).Result()
	if err != nil {
		t.Fatalf("Get: %v", err)
	}
	if strings.Contains(raw, "correlation_id") {
		t.Fatalf("job state should omit the empty field, got %s", raw)
	}
}
