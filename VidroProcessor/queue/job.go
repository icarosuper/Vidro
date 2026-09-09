package queue

import (
	"context"
	"encoding/json"
	"fmt"
	"time"
)

// VideoMetadata mirrors the metadata extracted by the pipeline analysis step.
type VideoMetadata struct {
	Duration   float64 `json:"duration"`
	Width      int     `json:"width"`
	Height     int     `json:"height"`
	VideoCodec string  `json:"video_codec"`
	AudioCodec string  `json:"audio_codec"`
	FPS        float64 `json:"fps"`
	Bitrate    int64   `json:"bitrate"`
	Size       int64   `json:"size"`
}

// JobStatus represents the state of a processing job.
type JobStatus string

const (
	JobStatusPending    JobStatus = "pending"
	JobStatusProcessing JobStatus = "processing"
	JobStatusDone       JobStatus = "done"
	JobStatusFailed     JobStatus = "failed"

	// MaxJobRetries is the maximum number of retries after the initial attempt.
	MaxJobRetries = 3

	jobTTL = 24 * time.Hour
)

// JobArtifacts contains the paths of the artifacts generated in MinIO.
type JobArtifacts struct {
	Video      string `json:"video,omitempty"`
	Thumbnails string `json:"thumbnails,omitempty"`
	Audio      string `json:"audio,omitempty"`
	Preview    string `json:"preview,omitempty"`
	HLS        string `json:"hls,omitempty"`
}

// JobState represents the complete state of a processing job.
type JobState struct {
	Status      JobStatus      `json:"status"`
	Error       string         `json:"error,omitempty"`
	Artifacts   *JobArtifacts  `json:"artifacts,omitempty"`
	Metadata    *VideoMetadata `json:"metadata,omitempty"`
	RetryCount  int            `json:"retry_count"`
	CallbackURL string         `json:"callback_url,omitempty"`
	CreatedAt   int64          `json:"created_at"`
	UpdatedAt   int64          `json:"updated_at"`
}

func jobKey(videoID string) string {
	return "job:" + videoID
}

func setJobState(ctx context.Context, videoID string, state JobState) error {
	state.UpdatedAt = time.Now().Unix()
	data, err := json.Marshal(state)
	if err != nil {
		return fmt.Errorf("failed to serialize job state: %w", err)
	}
	return client.Set(ctx, jobKey(videoID), string(data), jobTTL).Err()
}

// PublishJob publishes a videoID to the queue and records the initial state as pending.
// callbackURL is optional: if non-empty, the worker will notify this URL upon completion.
// Should be called by the producer (API) when submitting a video for processing.
func PublishJob(ctx context.Context, videoID, callbackURL string) error {
	state := JobState{
		Status:      JobStatusPending,
		CallbackURL: callbackURL,
		CreatedAt:   time.Now().Unix(),
	}
	if err := setJobState(ctx, videoID, state); err != nil {
		return fmt.Errorf("failed to create job state: %w", err)
	}
	return client.LPush(ctx, cfg.ProcessingRequestQueue, videoID).Err()
}

// SetJobProcessing updates the job state to processing.
func SetJobProcessing(ctx context.Context, videoID string) error {
	existing, _ := GetJobState(ctx, videoID)
	if existing == nil {
		existing = &JobState{CreatedAt: time.Now().Unix()}
	}
	existing.Status = JobStatusProcessing
	return setJobState(ctx, videoID, *existing)
}

// SetJobDone updates the job state to done with the generated artifacts and metadata.
func SetJobDone(ctx context.Context, videoID string, artifacts JobArtifacts, metadata *VideoMetadata) error {
	existing, _ := GetJobState(ctx, videoID)
	if existing == nil {
		existing = &JobState{CreatedAt: time.Now().Unix()}
	}
	existing.Status = JobStatusDone
	existing.Artifacts = &artifacts
	existing.Metadata = metadata
	existing.Error = ""
	return setJobState(ctx, videoID, *existing)
}

// SetJobFailed updates the job state to failed, increments the retry counter,
// and returns the updated state so the caller can decide between retry and DLQ.
func SetJobFailed(ctx context.Context, videoID string, jobErr error) (*JobState, error) {
	existing, _ := GetJobState(ctx, videoID)
	if existing == nil {
		existing = &JobState{CreatedAt: time.Now().Unix()}
	}
	existing.Status = JobStatusFailed
	existing.Error = jobErr.Error()
	existing.RetryCount++
	if err := setJobState(ctx, videoID, *existing); err != nil {
		return nil, err
	}
	return existing, nil
}

// ShouldRetry reports whether the job still has retry budget left.
// RetryCount is incremented before this is consulted, so attempt N+1 of a job
// that already failed N times is allowed while N <= MaxJobRetries: initial
// attempt plus MaxJobRetries retries.
func (s *JobState) ShouldRetry() bool {
	return s.RetryCount <= MaxJobRetries
}

// RequeueJob puts the job back in the main queue for reprocessing.
// AcknowledgeMessage must still be called to remove it from the processing queue.
func RequeueJob(ctx context.Context, videoID string) error {
	existing, _ := GetJobState(ctx, videoID)
	if existing != nil {
		existing.Status = JobStatusPending
		if err := setJobState(ctx, videoID, *existing); err != nil {
			return fmt.Errorf("failed to update state for requeue: %w", err)
		}
	}
	return client.LPush(ctx, cfg.ProcessingRequestQueue, videoID).Err()
}

// MoveToDLQ moves the job to the dead letter queue after exhausting retries.
// AcknowledgeMessage must still be called to remove it from the processing queue.
func MoveToDLQ(ctx context.Context, videoID string) error {
	return client.LPush(ctx, deadLetterQueueName(), videoID).Err()
}

// GetJobState returns the current state of a job. Returns nil if the job does not exist.
func GetJobState(ctx context.Context, videoID string) (*JobState, error) {
	data, err := client.Get(ctx, jobKey(videoID)).Bytes()
	if err != nil {
		return nil, fmt.Errorf("job not found: %w", err)
	}
	var state JobState
	if err := json.Unmarshal(data, &state); err != nil {
		return nil, fmt.Errorf("failed to deserialize job state: %w", err)
	}
	return &state, nil
}
