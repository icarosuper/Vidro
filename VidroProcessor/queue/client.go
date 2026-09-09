package queue

import (
	"context"
	"fmt"
	"time"

	"github.com/redis/go-redis/v9"
	"github.com/rs/zerolog/log"

	"video-processor/config"
	"video-processor/internal/circuitbreaker"
)

var (
	client *redis.Client
	cfg    *config.Config
)

type Message struct {
	VideoID string
}

func InitRedisClient(configs *config.Config) {
	cfg = configs

	client = redis.NewClient(&redis.Options{
		Addr: cfg.RedisHost,
	})

	if err := client.Ping(context.Background()).Err(); err != nil {
		log.Fatal().Err(err).Msg("Failed to connect Redis client")
	}
	log.Info().Str("host", cfg.RedisHost).Msg("Redis client connected successfully")
}

func processingQueueName() string {
	return cfg.ProcessingRequestQueue + ":processing"
}

func deadLetterQueueName() string {
	return cfg.ProcessingRequestQueue + ":dead"
}

// ConsumeMessage blocks until a message is received from the queue or ctx is canceled.
// Uses BRPOPLPUSH to atomically move the job to the processing queue.
func ConsumeMessage(ctx context.Context) (*Message, error) {
	result, err := circuitbreaker.Redis.Execute(func() (interface{}, error) {
		// BRPOPLPUSH is deliberate, not legacy: see docs/agents/design-decisions.md #1.
		// BLMOVE is the modern spelling but needs Redis 6.2+, which nothing here guarantees.
		//nolint:staticcheck // SA1019: BLMove needs Redis 6.2+, which nothing here guarantees
		videoID, err := client.BRPopLPush(ctx, cfg.ProcessingRequestQueue, processingQueueName(), 0).Result()
		if err != nil {
			return nil, err
		}
		return &Message{VideoID: videoID}, nil
	})
	if err != nil {
		return nil, err
	}
	message, isMessage := result.(*Message)
	if !isMessage {
		return nil, fmt.Errorf("circuit breaker returned %T, expected *Message", result)
	}
	return message, nil
}

// AcknowledgeMessage removes the job from the processing queue after completion (success or failure).
func AcknowledgeMessage(ctx context.Context, videoID string) error {
	_, err := circuitbreaker.Redis.Execute(func() (interface{}, error) {
		return nil, client.LRem(ctx, processingQueueName(), 1, videoID).Err()
	})
	return err
}

func PublishSuccessMessage(ctx context.Context, videoID string) error {
	_, err := circuitbreaker.Redis.Execute(func() (interface{}, error) {
		return nil, client.LPush(ctx, cfg.ProcessingFinishedQueue, videoID).Err()
	})
	return err
}

// StartRecovery starts a goroutine that periodically checks the processing queue
// and re-queues stuck jobs (worker crash) back to the main queue.
func StartRecovery(ctx context.Context, stuckTimeout time.Duration) {
	ticker := time.NewTicker(time.Minute)
	defer ticker.Stop()
	log.Info().Dur("stuck_timeout", stuckTimeout).Msg("Orphan job recovery started")
	for {
		select {
		case <-ctx.Done():
			return
		case <-ticker.C:
			recoverStuckJobs(ctx, stuckTimeout)
		}
	}
}

func recoverStuckJobs(ctx context.Context, stuckTimeout time.Duration) {
	videoIDs, err := client.LRange(ctx, processingQueueName(), 0, -1).Result()
	if err != nil {
		log.Warn().Err(err).Msg("Failed to check processing queue for recovery")
		return
	}

	threshold := time.Now().Add(-stuckTimeout).Unix()
	for _, videoID := range videoIDs {
		state, err := GetJobState(ctx, videoID)
		if err != nil || state == nil {
			continue
		}
		if state.Status != JobStatusProcessing || state.UpdatedAt >= threshold {
			continue
		}

		state.RetryCount++

		// A job that hangs the worker every time orphans every time. Without this
		// check recovery would re-queue it forever and it would never reach the DLQ,
		// unlike the failure path in the worker, which does honour the budget.
		if !state.ShouldRetry() {
			log.Error().Str("videoID", videoID).Int("retry_count", state.RetryCount).Msg("Orphan job exhausted retries, moving to dead letter queue")
			state.Status = JobStatusFailed
			state.Error = "orphaned repeatedly: retries exhausted during recovery"
			if err := setJobState(ctx, videoID, *state); err != nil {
				log.Warn().Err(err).Str("videoID", videoID).Msg("Failed to update state during recovery")
				continue
			}
			if err := MoveToDLQ(ctx, videoID); err != nil {
				log.Warn().Err(err).Str("videoID", videoID).Msg("Failed to move orphan job to dead letter queue")
				continue
			}
			client.LRem(ctx, processingQueueName(), 1, videoID)
			continue
		}

		log.Warn().Str("videoID", videoID).Int("retry_count", state.RetryCount).Msg("Orphan job detected, re-queuing")

		state.Status = JobStatusPending
		if err := setJobState(ctx, videoID, *state); err != nil {
			log.Warn().Err(err).Str("videoID", videoID).Msg("Failed to update state during recovery")
			continue
		}
		client.LRem(ctx, processingQueueName(), 1, videoID)
		client.LPush(ctx, cfg.ProcessingRequestQueue, videoID)
	}
}

// GetQueueSize returns the number of jobs waiting in the request queue.
func GetQueueSize(ctx context.Context) (int64, error) {
	return client.LLen(ctx, cfg.ProcessingRequestQueue).Result()
}

// HealthCheck checks whether the Redis client is healthy.
func HealthCheck(ctx context.Context) error {
	return client.Ping(ctx).Err()
}
