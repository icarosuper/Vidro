package queue

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"strings"
	"testing"
	"time"

	"github.com/alicebob/miniredis/v2"
	"github.com/alicebob/miniredis/v2/server"
	"github.com/redis/go-redis/v9"
	"github.com/rs/zerolog"
	"github.com/rs/zerolog/log"

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
		RedisHost:              mr.Addr(),
		ProcessingRequestQueue: "videos",
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

// Same rule as SetJobProcessing: a state recreated here would have no callback_url.
func TestSetJobDoneAndFailed_WithoutExistingStateRefuseAndCreateNothing(t *testing.T) {
	mr := setupRedis(t)

	_, failedErr := SetJobFailed(t.Context(), "ghost", errBoom)
	doneErr := SetJobDone(t.Context(), "ghost", JobArtifacts{}, nil)

	if !errors.Is(failedErr, ErrJobStateMissing) {
		t.Errorf("SetJobFailed error = %v, want %v", failedErr, ErrJobStateMissing)
	}
	if !errors.Is(doneErr, ErrJobStateMissing) {
		t.Errorf("SetJobDone error = %v, want %v", doneErr, ErrJobStateMissing)
	}
	stateWasCreated := mr.Exists(jobKey("ghost"))
	if stateWasCreated {
		t.Errorf("a status write created %s without a callback_url", jobKey("ghost"))
	}
}

// A Redis failure says nothing about whether the state exists, so it must not read as "missing":
// callers dead-letter on ErrJobStateMissing.
func TestGetJobState_DistinguishesMissingFromRedisFailure(t *testing.T) {
	mr := setupRedis(t)

	_, missingErr := GetJobState(t.Context(), "ghost")
	if !errors.Is(missingErr, ErrJobStateMissing) {
		t.Fatalf("missing state error = %v, want %v", missingErr, ErrJobStateMissing)
	}

	mr.Close()
	_, redisDownErr := GetJobState(t.Context(), "ghost")
	if redisDownErr == nil {
		t.Fatal("expected an error with Redis down")
	}
	if errors.Is(redisDownErr, ErrJobStateMissing) {
		t.Fatalf("Redis failure reported as missing state: %v", redisDownErr)
	}
}

// The callback_url lives only in job:<id>. Recreating a missing state would let the worker
// process the video and notify nobody, so SetJobProcessing must refuse instead.
func TestSetJobProcessing_WithoutExistingStateRefusesAndCreatesNothing(t *testing.T) {
	mr := setupRedis(t)

	err := SetJobProcessing(t.Context(), "ghost")

	if !errors.Is(err, ErrJobStateMissing) {
		t.Fatalf("SetJobProcessing error = %v, want %v", err, ErrJobStateMissing)
	}
	stateWasCreated := mr.Exists(jobKey("ghost"))
	if stateWasCreated {
		t.Fatalf("SetJobProcessing created %s without a callback_url", jobKey("ghost"))
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

	if err := setJobState(t.Context(), "vid", JobState{Status: JobStatusProcessing}); err != nil {
		t.Fatalf("setJobState: %v", err)
	}
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

	RecoverStuckJobs(t.Context(), 30*time.Minute, nil)

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

// TestRecoverStuckJobs_FailedRequeueIsRetriedOnNextSweep: when the move fails, the job must
// still look orphaned (status processing) so the next sweep picks it up. Writing "pending"
// before the move left it in :processing with a status recovery skips, forever.
func TestRecoverStuckJobs_FailedRequeueIsRetriedOnNextSweep(t *testing.T) {
	mr := setupRedis(t)
	parkInProcessing(t, "vid", JobStatusProcessing, 1, time.Hour)

	mr.Server().SetPreHook(func(c *server.Peer, cmd string, args ...string) bool {
		if cmd == "EVALSHA" || cmd == "EVAL" {
			// Connection dies before the move script runs: nothing is applied, as in a real outage.
			c.Close()
			return true
		}
		return false
	})
	RecoverStuckJobs(t.Context(), 30*time.Minute, nil)

	if got := listOf(t, processingQueueName()); len(got) != 1 {
		t.Fatalf("processing queue = %v, want [vid] after failed move", got)
	}
	state, err := GetJobState(t.Context(), "vid")
	if err != nil {
		t.Fatalf("GetJobState: %v", err)
	}
	if state.Status != JobStatusProcessing {
		t.Fatalf("Status = %q after failed move, want %q", state.Status, JobStatusProcessing)
	}

	mr.Server().SetPreHook(nil)
	RecoverStuckJobs(t.Context(), 30*time.Minute, nil)

	if got := listOf(t, cfg.ProcessingRequestQueue); len(got) != 1 || got[0] != "vid" {
		t.Fatalf("request queue = %v, want [vid] after the second sweep", got)
	}
}

// TestRecoverStuckJobs_FailedDeadLetterMoveIsRetriedOnNextSweep: same rule as the re-queue
// branch. A Failed state written before a failed move would strand the job in :processing.
func TestRecoverStuckJobs_FailedDeadLetterMoveIsRetriedOnNextSweep(t *testing.T) {
	mr := setupRedis(t)
	parkInProcessing(t, "vid", JobStatusProcessing, MaxJobRetries, time.Hour)

	mr.Server().SetPreHook(func(c *server.Peer, cmd string, args ...string) bool {
		if cmd == "EVALSHA" || cmd == "EVAL" {
			c.Close()
			return true
		}
		return false
	})
	RecoverStuckJobs(t.Context(), 30*time.Minute, nil)

	state, err := GetJobState(t.Context(), "vid")
	if err != nil {
		t.Fatalf("GetJobState: %v", err)
	}
	if state.Status != JobStatusProcessing {
		t.Fatalf("Status = %q after failed move, want %q", state.Status, JobStatusProcessing)
	}

	mr.Server().SetPreHook(nil)
	RecoverStuckJobs(t.Context(), 30*time.Minute, nil)

	if got := listOf(t, deadLetterQueueName()); len(got) != 1 || got[0] != "vid" {
		t.Fatalf("dead letter queue = %v, want [vid] after the second sweep", got)
	}
}

// TestRecoverStuckJobs_ExhaustedOrphanGoesToDLQ covers the mirror of the
// worker's failure path: a video that hangs the worker orphans every time, so
// without a budget check recovery would re-queue it forever.
func TestRecoverStuckJobs_ExhaustedOrphanGoesToDLQ(t *testing.T) {
	setupRedis(t)
	parkInProcessing(t, "vid", JobStatusProcessing, MaxJobRetries, time.Hour)

	RecoverStuckJobs(t.Context(), 30*time.Minute, nil)

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

// The API only learns about a dead-lettered orphan through the callback: without it the video
// stays in Processing until the API's own timeout. A requeued orphan must not fire it.
func TestRecoverStuckJobs_NotifiesOnlyWhenOrphanIsDeadLettered(t *testing.T) {
	setupRedis(t)
	parkInProcessing(t, "exhausted", JobStatusProcessing, MaxJobRetries, time.Hour)
	parkInProcessing(t, "retryable", JobStatusProcessing, 0, time.Hour)

	notified := map[string]JobStatus{}
	RecoverStuckJobs(t.Context(), 30*time.Minute, func(videoID string, state *JobState) {
		notified[videoID] = state.Status
	})

	if len(notified) != 1 || notified["exhausted"] != JobStatusFailed {
		t.Fatalf("notified = %v, want only exhausted, with status failed", notified)
	}
}

// A job parked in :processing whose job:<id> expired has nobody to notify and no age to judge
// by. Leaving it there meant it stayed forever: recovery skipped it on every sweep.
func TestRecoverStuckJobs_JobWithoutStateGoesToDLQ(t *testing.T) {
	setupRedis(t)
	if err := client.LPush(context.Background(), processingQueueName(), "vid").Err(); err != nil {
		t.Fatalf("LPush: %v", err)
	}

	RecoverStuckJobs(t.Context(), 30*time.Minute, nil)

	if got := listOf(t, deadLetterQueueName()); len(got) != 1 || got[0] != "vid" {
		t.Fatalf("dead letter queue = %v, want [vid]", got)
	}
	if got := listOf(t, processingQueueName()); len(got) != 0 {
		t.Fatalf("processing queue = %v, want empty", got)
	}
	if got := listOf(t, cfg.ProcessingRequestQueue); len(got) != 0 {
		t.Fatalf("request queue = %v, want empty", got)
	}
}

func TestRecoverStuckJobs_LeavesHealthyJobsAlone(t *testing.T) {
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
	} {
		t.Run(tc.name, func(t *testing.T) {
			setupRedis(t)
			tc.setup(t)

			RecoverStuckJobs(t.Context(), 30*time.Minute, nil)

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
		"PublishJob":         func() error { return PublishJob(canceled, "vid", "", "") },
		"SetJobProcessing":   func() error { return SetJobProcessing(canceled, "vid") },
		"SetJobDone":         func() error { return SetJobDone(canceled, "vid", JobArtifacts{}, nil) },
		"SetJobFailed":       func() error { _, err := SetJobFailed(canceled, "vid", errBoom); return err },
		"RequeueJob":         func() error { return RequeueJob(canceled, "vid") },
		"MoveToDLQ":          func() error { return MoveToDLQ(canceled, "vid") },
		"GetJobState":        func() error { _, err := GetJobState(canceled, "vid"); return err },
		"AcknowledgeMessage": func() error { return AcknowledgeMessage(canceled, "vid") },
		"GetQueueSize":       func() error { _, err := GetQueueSize(canceled); return err },
		"HealthCheck":        func() error { return HealthCheck(canceled) },
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

	RecoverStuckJobs(canceled, 30*time.Minute, nil)

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

func TestRequeueJob_MissingStateRefusesAndQueuesNothing(t *testing.T) {
	setupRedis(t)

	err := RequeueJob(t.Context(), "gone")
	if !errors.Is(err, ErrJobStateMissing) {
		t.Fatalf("err = %v, want ErrJobStateMissing", err)
	}
	if got := listOf(t, cfg.ProcessingRequestQueue); len(got) != 0 {
		t.Fatalf("request queue = %v, want empty", got)
	}
}

// A read failure that is not "missing" must not requeue a job whose status was left unchanged.
func TestRequeueJob_ReadFailurePropagatesAndQueuesNothing(t *testing.T) {
	mr := setupRedis(t)
	mr.Set(jobKey("vid"), "{not json")

	err := RequeueJob(t.Context(), "vid")
	if err == nil || errors.Is(err, ErrJobStateMissing) {
		t.Fatalf("err = %v, want a non-missing read error", err)
	}
	if got := listOf(t, cfg.ProcessingRequestQueue); len(got) != 0 {
		t.Fatalf("request queue = %v, want empty", got)
	}
}

func TestMoveFromProcessing_MovesAtomicallyAndReportsFailure(t *testing.T) {
	mr := setupRedis(t)
	if err := client.LPush(t.Context(), processingQueueName(), "vid").Err(); err != nil {
		t.Fatalf("LPush: %v", err)
	}
	if err := moveFromProcessing(t.Context(), "vid", cfg.ProcessingRequestQueue); err != nil {
		t.Fatalf("moveFromProcessing: %v", err)
	}
	if got := listOf(t, processingQueueName()); len(got) != 0 {
		t.Fatalf("processing queue = %v, want empty", got)
	}
	if got := listOf(t, cfg.ProcessingRequestQueue); len(got) != 1 || got[0] != "vid" {
		t.Fatalf("request queue = %v, want [vid]", got)
	}

	mr.Close()
	if err := moveFromProcessing(t.Context(), "vid", cfg.ProcessingRequestQueue); err == nil {
		t.Fatal("moveFromProcessing returned nil with Redis down")
	}
}

// Lines written outside the job frame (recovery, webhook) must still carry the correlationID,
// or the upload's trail in Loki breaks exactly when something went wrong.
func TestJobStateLogger_CarriesCorrelationIDWhenPresent(t *testing.T) {
	var output bytes.Buffer
	previous := log.Logger
	log.Logger = zerolog.New(&output)
	t.Cleanup(func() { log.Logger = previous })

	withID := JobState{CorrelationID: "req-42"}
	idLogger := withID.Logger("vid")
	idLogger.Info().Msg("a")
	withoutID := JobState{}
	plainLogger := withoutID.Logger("vid")
	plainLogger.Info().Msg("b")

	lines := strings.Split(strings.TrimSpace(output.String()), "\n")
	if len(lines) != 2 {
		t.Fatalf("got %d log lines, want 2: %q", len(lines), output.String())
	}
	if !strings.Contains(lines[0], `"videoID":"vid"`) || !strings.Contains(lines[0], `"correlationID":"req-42"`) {
		t.Errorf("line with an ID = %s, want videoID and correlationID", lines[0])
	}
	if !strings.Contains(lines[1], `"videoID":"vid"`) || strings.Contains(lines[1], "correlationID") {
		t.Errorf("line without an ID = %s, want videoID and no correlationID field", lines[1])
	}
}

// MULTI/EXEC would apply the LREM even though the LPUSH fails at execution time: the job would
// vanish from both queues. It must stay in :processing so the next sweep tries again.
func TestMoveFromProcessing_FailedPushKeepsJobInProcessing(t *testing.T) {
	mr := setupRedis(t)
	parkInProcessing(t, "vid", JobStatusProcessing, 0, time.Hour)
	if err := mr.Set(deadLetterQueueName(), "not a list"); err != nil {
		t.Fatalf("Set: %v", err)
	}

	if err := moveFromProcessing(t.Context(), "vid", deadLetterQueueName()); err == nil {
		t.Fatal("moveFromProcessing returned nil although the destination is not a list")
	}

	if got := listOf(t, processingQueueName()); len(got) != 1 || got[0] != "vid" {
		t.Fatalf("processing queue = %v, want [vid]: the job was lost", got)
	}
}
