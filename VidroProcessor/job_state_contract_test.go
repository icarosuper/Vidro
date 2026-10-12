package main

import (
	"bufio"
	"bytes"
	"encoding/json"
	"os"
	"path/filepath"
	"reflect"
	"strings"
	"testing"

	"github.com/alicebob/miniredis/v2"

	"video-processor/config"
	"video-processor/queue"
)

// The job VidroApi publishes is this worker's only input: a video id on the queue plus the
// envelope under job:<id>, which carries the callback_url and the correlation_id. The golden in
// contracts/job-state.json is the contract: JobStateContractTests.cs proves the API publishes
// exactly it, this proves the worker reads exactly it. A field renamed on one side turns red on
// both — a lost callback_url means a processed video nobody is told about.

type jobStateGolden struct {
	Queue    string          `json:"queue"`
	StateKey string          `json:"stateKey"`
	State    json.RawMessage `json:"state"`
}

func loadJobStateGolden(t *testing.T) jobStateGolden {
	t.Helper()
	var golden jobStateGolden
	if err := json.Unmarshal(contractGolden(t, "job-state.json"), &golden); err != nil {
		t.Fatalf("failed to parse golden: %v", err)
	}
	return golden
}

func TestJobState_ReadsContractGolden(t *testing.T) {
	golden := loadJobStateGolden(t)

	// Through the real lookup, so the job:<id> key prefix is part of what is checked.
	mr := miniredis.RunT(t)
	queue.InitRedisClient(&config.Config{RedisHost: mr.Addr(), ProcessingRequestQueue: golden.Queue})
	if err := mr.Set(golden.StateKey, string(golden.State)); err != nil {
		t.Fatalf("failed to seed job state: %v", err)
	}
	state, err := queue.GetJobState(t.Context(), contractVideoID)
	if err != nil {
		t.Fatalf("GetJobState under %s: %v", golden.StateKey, err)
	}

	// A plain Unmarshal drops a field the worker does not know, so an API-side rename would come
	// back as an empty CallbackURL and pass. Refuse unknown fields instead.
	decoder := json.NewDecoder(bytes.NewReader(golden.State))
	decoder.DisallowUnknownFields()
	var strict queue.JobState
	if err := decoder.Decode(&strict); err != nil {
		t.Fatalf("golden has a field queue.JobState does not read: %v", err)
	}
	if !reflect.DeepEqual(*state, strict) {
		t.Fatalf("GetJobState = %+v, want %+v", *state, strict)
	}

	// And the other direction: a json tag renamed here shows up as a missing key.
	reencoded, err := json.Marshal(strict)
	if err != nil {
		t.Fatalf("failed to re-encode: %v", err)
	}
	var want, got map[string]any
	if err := json.Unmarshal(golden.State, &want); err != nil {
		t.Fatalf("failed to parse golden state: %v", err)
	}
	if err := json.Unmarshal(reencoded, &got); err != nil {
		t.Fatalf("failed to parse re-encoded state: %v", err)
	}
	if !reflect.DeepEqual(got, want) {
		t.Fatalf("job state diverged from contracts/job-state.json\n--- expected ---\n%s\n--- got ---\n%s",
			golden.State, reencoded)
	}
}

// The worker's queue name lives only in deployment config, so that is what is checked: the stack
// (root docker-compose.yml) and the local template (.env-example).
func TestProcessingRequestQueue_MatchesContractGolden(t *testing.T) {
	golden := loadJobStateGolden(t)

	configFiles := []string{filepath.Join("..", "docker-compose.yml"), ".env-example"}
	for _, path := range configFiles {
		queueName := processingRequestQueueIn(t, path)
		if queueName != golden.Queue {
			t.Errorf("%s sets PROCESSING_REQUEST_QUEUE=%q, contracts/job-state.json says %q",
				path, queueName, golden.Queue)
		}
	}
}

// processingRequestQueueIn reads the value of PROCESSING_REQUEST_QUEUE from a compose file
// (`KEY: value`) or an env file (`KEY=value`).
func processingRequestQueueIn(t *testing.T, path string) string {
	t.Helper()
	file, err := os.Open(path)
	if err != nil {
		t.Fatalf("failed to open %s: %v", path, err)
	}
	defer file.Close()

	scanner := bufio.NewScanner(file)
	for scanner.Scan() {
		line := strings.TrimSpace(scanner.Text())
		value, found := strings.CutPrefix(line, "PROCESSING_REQUEST_QUEUE")
		if !found {
			continue
		}
		value = strings.TrimLeft(value, ":= ")
		return strings.Trim(value, `"'`)
	}
	t.Fatalf("PROCESSING_REQUEST_QUEUE not set in %s", path)
	return ""
}
