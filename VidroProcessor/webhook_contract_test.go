package main

import (
	"encoding/json"
	"os"
	"path/filepath"
	"testing"

	"video-processor/queue"
)

// The `video-processed` webhook body is a contract with VidroApi. The golden files in
// contracts/ are the contract itself: this test proves the worker emits exactly them, and
// VideoProcessedTests.cs proves the API accepts exactly them. A field renamed on one side
// turns red on both.
//
// Both P0 bugs in TODO.md were divergences on this boundary, unnoticed for months.

const contractVideoID = "11111111-1111-1111-1111-111111111111"

func contractGolden(t *testing.T, name string) []byte {
	t.Helper()
	path := filepath.Join("..", "contracts", name)
	content, err := os.ReadFile(path)
	if err != nil {
		t.Fatalf("failed to read golden %s: %v", path, err)
	}
	return content
}

func TestBuildWebhookPayload_MatchesContractGoldens(t *testing.T) {
	fullArtifacts := &queue.JobArtifacts{
		Video:      "processed/" + contractVideoID + "_processed",
		Thumbnails: "thumbnails/" + contractVideoID,
		Audio:      "audio/" + contractVideoID + ".mp3",
		Preview:    "preview/" + contractVideoID + "_preview.mp4",
		HLS:        "hls/" + contractVideoID,
	}
	fullMetadata := &queue.VideoMetadata{
		Duration:   120.5,
		Width:      1920,
		Height:     1080,
		VideoCodec: "h264",
		AudioCodec: "aac",
		FPS:        30,
		Bitrate:    664_000,
		Size:       10_000_000,
	}

	cases := []struct {
		golden string
		state  queue.JobState
	}{
		{
			// Whole pipeline succeeded.
			golden: "video-processed-success-full.json",
			state: queue.JobState{
				Status:    queue.JobStatusDone,
				Artifacts: fullArtifacts,
				Metadata:  fullMetadata,
			},
		},
		{
			// Every non-critical step failed, `analyze` included: no optional artifact,
			// no metadata block at all.
			golden: "video-processed-success-minimal.json",
			state: queue.JobState{
				Status:    queue.JobStatusDone,
				Artifacts: &queue.JobArtifacts{Video: "processed/" + contractVideoID + "_processed"},
			},
		},
		{
			// Transcode output missing — a success that is not a success. The API must
			// mark the video as Failed instead of throwing.
			golden: "video-processed-success-without-processed-path.json",
			state:  queue.JobState{Status: queue.JobStatusDone},
		},
		{
			// Permanent failure: retries exhausted, job in the DLQ.
			golden: "video-processed-failure.json",
			state:  queue.JobState{Status: queue.JobStatusFailed, Error: "transcode failed"},
		},
	}

	for _, tc := range cases {
		t.Run(tc.golden, func(t *testing.T) {
			payload := buildWebhookPayload(contractVideoID, &tc.state)

			// Indented to match the golden byte for byte; the wire body is the same JSON
			// compacted by json.Marshal inside webhook.Notify.
			serialized, err := json.MarshalIndent(payload, "", "  ")
			if err != nil {
				t.Fatalf("failed to serialize payload: %v", err)
			}
			serialized = append(serialized, '\n')

			expected := contractGolden(t, tc.golden)
			if string(serialized) != string(expected) {
				t.Fatalf("payload diverged from contracts/%s\n--- expected ---\n%s\n--- got ---\n%s",
					tc.golden, expected, serialized)
			}
		})
	}
}
