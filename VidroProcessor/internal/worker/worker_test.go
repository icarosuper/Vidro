package worker

import (
	"context"
	"encoding/json"
	"errors"
	"testing"
	"time"

	"github.com/alicebob/miniredis/v2"

	"video-processor/config"
	"video-processor/internal/processor"
	"video-processor/minio"
	"video-processor/queue"
)

const (
	requestQueue    = "videos"
	processingQueue = requestQueue + ":processing"
	deadLetterQueue = requestQueue + ":dead"
	finishedQueue   = "videos:finished"
	videoID         = "vid"
)

var errDownloadFailed = errors.New("object not found")

// fakeStorage stands in for MinIO. Every call succeeds except DownloadVideo, which runs
// download when set — that is where each test decides how the job goes.
type fakeStorage struct {
	download func(ctx context.Context) error
}

func (s fakeStorage) DownloadVideo(ctx context.Context, _ minio.VideoType, _, _ string) error {
	if s.download == nil {
		return nil
	}
	return s.download(ctx)
}

func (fakeStorage) UploadVideo(context.Context, string, minio.VideoType, string) error { return nil }

func (fakeStorage) ArchiveRawVideo(context.Context, string) error { return nil }

func (fakeStorage) UploadDirectory(context.Context, string, string) error { return nil }

func (fakeStorage) UploadFile(context.Context, string, string) error { return nil }

func succeedingPipeline(context.Context, string, string, processor.Options) (*processor.ProcessingResult, error) {
	return &processor.ProcessingResult{}, nil
}

// setupQueue points the queue package at an in-process Redis — same harness as
// queue/queue_test.go, reached through InitRedisClient because the globals are not ours.
func setupQueue(t *testing.T) (*miniredis.Miniredis, *config.Config) {
	t.Helper()
	mr := miniredis.RunT(t)
	cfg := &config.Config{
		RedisHost:               mr.Addr(),
		ProcessingRequestQueue:  requestQueue,
		ProcessingFinishedQueue: finishedQueue,
		JobTimeout:              time.Minute,
	}
	queue.InitRedisClient(cfg)
	return mr, cfg
}

func newTestWorker(cfg *config.Config, storage storage) *Worker {
	return &Worker{cfg: cfg, storage: storage, processVideo: succeedingPipeline}
}

func publishJob(t *testing.T) {
	t.Helper()
	if err := queue.PublishJob(t.Context(), videoID, "", ""); err != nil {
		t.Fatalf("PublishJob: %v", err)
	}
}

func listOf(t *testing.T, mr *miniredis.Miniredis, key string) []string {
	t.Helper()
	keyExists := mr.Exists(key)
	if !keyExists {
		return nil
	}
	items, err := mr.List(key)
	if err != nil {
		t.Fatalf("List %s: %v", key, err)
	}
	return items
}

func jobState(t *testing.T) *queue.JobState {
	t.Helper()
	state, err := queue.GetJobState(t.Context(), videoID)
	if err != nil {
		t.Fatalf("GetJobState: %v", err)
	}
	return state
}

// waitForProcessingQueueToDrain polls because the ack runs in the job goroutine's defers,
// which may still be running when processNextMessage returns — the very window the #13 bug
// lived in. A job that is never acked times out here.
func waitForProcessingQueueToDrain(t *testing.T, mr *miniredis.Miniredis) {
	t.Helper()
	deadline := time.Now().Add(2 * time.Second)
	for time.Now().Before(deadline) {
		pendingInProcessing := listOf(t, mr, processingQueue)
		if len(pendingInProcessing) == 0 {
			return
		}
		time.Sleep(5 * time.Millisecond)
	}
	t.Fatalf("job never left %s: %v (bookkeeping did not close it out)",
		processingQueue, listOf(t, mr, processingQueue))
}

// TestProcessNextMessage_FailedJobIsClosedOut is the regression test for design-decisions #13:
// the parent used to cancel the bookkeeping context while the job goroutine was still closing
// the job out, so a failed job stayed in :processing with retry_count 0.
func TestProcessNextMessage_FailedJobIsClosedOut(t *testing.T) {
	failingDownload := fakeStorage{download: func(context.Context) error { return errDownloadFailed }}

	t.Run("retry budget left: requeued", func(t *testing.T) {
		mr, cfg := setupQueue(t)
		publishJob(t)

		err := newTestWorker(cfg, failingDownload).processNextMessage(t.Context(), 1)
		if !errors.Is(err, errDownloadFailed) {
			t.Fatalf("processNextMessage error = %v, want %v", err, errDownloadFailed)
		}

		waitForProcessingQueueToDrain(t, mr)
		state := jobState(t)
		if state.RetryCount != 1 {
			t.Fatalf("RetryCount = %d, want 1", state.RetryCount)
		}
		if state.Status != queue.JobStatusPending {
			t.Fatalf("Status = %q, want %q", state.Status, queue.JobStatusPending)
		}
		if got := listOf(t, mr, requestQueue); len(got) != 1 || got[0] != videoID {
			t.Fatalf("request queue = %v, want [%s]", got, videoID)
		}
	})

	t.Run("retry budget spent: dead-lettered", func(t *testing.T) {
		mr, cfg := setupQueue(t)
		publishJob(t)
		for range queue.MaxJobRetries {
			if _, err := queue.SetJobFailed(t.Context(), videoID, errDownloadFailed); err != nil {
				t.Fatalf("SetJobFailed: %v", err)
			}
		}

		err := newTestWorker(cfg, failingDownload).processNextMessage(t.Context(), 1)
		if !errors.Is(err, errDownloadFailed) {
			t.Fatalf("processNextMessage error = %v, want %v", err, errDownloadFailed)
		}

		waitForProcessingQueueToDrain(t, mr)
		state := jobState(t)
		if state.RetryCount != queue.MaxJobRetries+1 {
			t.Fatalf("RetryCount = %d, want %d", state.RetryCount, queue.MaxJobRetries+1)
		}
		if state.Status != queue.JobStatusFailed {
			t.Fatalf("Status = %q, want %q", state.Status, queue.JobStatusFailed)
		}
		if got := listOf(t, mr, deadLetterQueue); len(got) != 1 || got[0] != videoID {
			t.Fatalf("dead letter queue = %v, want [%s]", got, videoID)
		}
		if got := listOf(t, mr, requestQueue); len(got) != 0 {
			t.Fatalf("request queue = %v, want empty", got)
		}
	})
}

// TestProcessNextMessage_CompletedJobIsAcked is the other half of #13: with the bug, the ack of
// a successful job failed too, and the job sat in :processing until orphan recovery
// reprocessed it.
func TestProcessNextMessage_CompletedJobIsAcked(t *testing.T) {
	mr, cfg := setupQueue(t)
	publishJob(t)

	err := newTestWorker(cfg, fakeStorage{}).processNextMessage(t.Context(), 1)
	if err != nil {
		t.Fatalf("processNextMessage: %v", err)
	}

	waitForProcessingQueueToDrain(t, mr)
	state := jobState(t)
	if state.Status != queue.JobStatusDone {
		t.Fatalf("Status = %q, want %q", state.Status, queue.JobStatusDone)
	}
	if state.RetryCount != 0 {
		t.Fatalf("RetryCount = %d, want 0", state.RetryCount)
	}
	processedID := videoID + "_processed"
	if got := listOf(t, mr, finishedQueue); len(got) != 1 || got[0] != processedID {
		t.Fatalf("finished queue = %v, want [%s]", got, processedID)
	}
}

// ageJobState rewinds updated_at, standing in for the hour that passes before orphan
// recovery considers a job abandoned.
func ageJobState(t *testing.T, mr *miniredis.Miniredis, age time.Duration) {
	t.Helper()
	state := jobState(t)
	state.UpdatedAt = time.Now().Add(-age).Unix()
	serialized, err := json.Marshal(state)
	if err != nil {
		t.Fatalf("marshal: %v", err)
	}
	if err := mr.Set("job:"+videoID, string(serialized)); err != nil {
		t.Fatalf("Set: %v", err)
	}
}

// TestWorker_CrashMidJobIsRecoveredAndReprocessed is the failure path of the queue end to end:
// a worker dies mid-job, the job survives in :processing, orphan recovery hands it back to the
// request queue, and the next worker finishes it.
func TestWorker_CrashMidJobIsRecoveredAndReprocessed(t *testing.T) {
	mr, cfg := setupQueue(t)
	publishJob(t)

	// A crashed process never runs its defers: the download blocks forever and ignores ctx,
	// so the job goroutine never reaches the bookkeeping. It is leaked on purpose.
	downloadStarted := make(chan struct{})
	crashingDownload := fakeStorage{download: func(context.Context) error {
		close(downloadStarted)
		select {}
	}}
	workerCtx, killWorker := context.WithCancel(t.Context())
	crashedWorkerReturned := make(chan struct{})
	var crashedWorkerErr error
	go func() {
		defer close(crashedWorkerReturned)
		crashedWorkerErr = newTestWorker(cfg, crashingDownload).processNextMessage(workerCtx, 1)
	}()
	select {
	case <-downloadStarted:
	case <-crashedWorkerReturned:
		killWorker()
		t.Fatalf("worker returned before reaching the download: %v", crashedWorkerErr)
	}
	killWorker()
	<-crashedWorkerReturned

	if got := listOf(t, mr, processingQueue); len(got) != 1 || got[0] != videoID {
		t.Fatalf("processing queue after crash = %v, want [%s]", got, videoID)
	}
	if state := jobState(t); state.Status != queue.JobStatusProcessing {
		t.Fatalf("Status after crash = %q, want %q", state.Status, queue.JobStatusProcessing)
	}

	ageJobState(t, mr, time.Hour)
	queue.RecoverStuckJobs(t.Context(), 30*time.Minute)

	if got := listOf(t, mr, requestQueue); len(got) != 1 || got[0] != videoID {
		t.Fatalf("request queue after recovery = %v, want [%s]", got, videoID)
	}
	if state := jobState(t); state.RetryCount != 1 {
		t.Fatalf("RetryCount after recovery = %d, want 1", state.RetryCount)
	}

	err := newTestWorker(cfg, fakeStorage{}).processNextMessage(t.Context(), 2)
	if err != nil {
		t.Fatalf("processNextMessage on the recovered job: %v", err)
	}

	waitForProcessingQueueToDrain(t, mr)
	if state := jobState(t); state.Status != queue.JobStatusDone {
		t.Fatalf("Status after reprocessing = %q, want %q", state.Status, queue.JobStatusDone)
	}
	if got := listOf(t, mr, requestQueue); len(got) != 0 {
		t.Fatalf("request queue = %v, want empty", got)
	}
}
