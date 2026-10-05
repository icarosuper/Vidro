package main

import (
	"encoding/json"
	"testing"
	"time"

	"video-processor/config"
	"video-processor/internal/worker"
	"video-processor/queue"
)

// The API marks a video Failed once it sits in Processing longer than
// VideoSettings:ProcessingTimeoutMinutes, and then drops the success webhook that arrives late.
// That timeout must cover this worker's worst case with every retry spent, so the worst case
// lives in contracts/processing-timeout.json: this test proves the golden matches the worker,
// ProcessingTimeoutContractTests.cs proves the API's timeout stays above it.
func TestWorstCaseJobDuration_MatchesContractGolden(t *testing.T) {
	var golden struct {
		WorkerWorstCaseMinutes int `json:"workerWorstCaseMinutes"`
	}
	if err := json.Unmarshal(contractGolden(t, "processing-timeout.json"), &golden); err != nil {
		t.Fatalf("failed to parse golden: %v", err)
	}

	// Defaults: PROCESSING_TIMEOUT_SCALE=1, JOB_TIMEOUT derived.
	cfg := &config.Config{ProcessingTimeoutScale: 1}
	attempts := queue.MaxJobRetries + 1
	// The slowest attempt is an orphaned one: it is requeued only once OrphanThreshold has
	// passed, noticed on the next recovery tick. A timed-out attempt ends at JobTimeout, sooner.
	slowestAttempt := worker.OrphanThreshold(cfg) + queue.RecoveryInterval
	worstCase := time.Duration(attempts) * slowestAttempt

	goldenWorstCase := time.Duration(golden.WorkerWorstCaseMinutes) * time.Minute
	if worstCase != goldenWorstCase {
		t.Fatalf("worker worst case = %v, contracts/processing-timeout.json says %v — update the golden "+
			"and keep VidroApi's VideoSettings:ProcessingTimeoutMinutes above it", worstCase, goldenWorstCase)
	}
}
