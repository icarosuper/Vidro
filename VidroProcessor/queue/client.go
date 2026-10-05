package queue

import (
	"context"
	"errors"
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

// RecoveryInterval is how often StartRecovery sweeps the processing queue. It is part of the
// worst case an orphaned job can take, which the API's processing timeout must cover
// (contracts/processing-timeout.json).
const RecoveryInterval = time.Minute

// StartRecovery starts a goroutine that periodically checks the processing queue
// and re-queues stuck jobs (worker crash) back to the main queue.
func StartRecovery(ctx context.Context, stuckTimeout time.Duration) {
	ticker := time.NewTicker(RecoveryInterval)
	defer ticker.Stop()
	log.Info().Dur("stuck_timeout", stuckTimeout).Msg("Orphan job recovery started")
	for {
		select {
		case <-ctx.Done():
			return
		case <-ticker.C:
			RecoverStuckJobs(ctx, stuckTimeout)
		}
	}
}

// RecoverStuckJobs runs one recovery sweep: orphans older than stuckTimeout go back to the
// request queue, or to the DLQ once their retry budget is spent. StartRecovery calls it
// every minute; it is exported so the worker tests can drive a crash-and-recover cycle.
func RecoverStuckJobs(ctx context.Context, stuckTimeout time.Duration) {
	videoIDs, err := client.LRange(ctx, processingQueueName(), 0, -1).Result()
	if err != nil {
		log.Warn().Err(err).Msg("Failed to check processing queue for recovery")
		return
	}

	threshold := time.Now().Add(-stuckTimeout).Unix()
	for _, videoID := range videoIDs {
		state, err := GetJobState(ctx, videoID)
		stateMissing := errors.Is(err, ErrJobStateMissing)
		if stateMissing {
			deadLetterOrphanWithoutState(ctx, videoID)
			continue
		}
		if err != nil {
			log.Warn().Err(err).Str("videoID", videoID).Msg("Failed to read job state during recovery")
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
			if err := moveFromProcessing(ctx, videoID, deadLetterQueueName()); err != nil {
				log.Warn().Err(err).Str("videoID", videoID).Msg("Failed to move orphan job to dead letter queue")
			}
			continue
		}

		log.Warn().Str("videoID", videoID).Int("retry_count", state.RetryCount).Msg("Orphan job detected, re-queuing")

		// Move first, state second. If the move fails the state is still "processing" and
		// old, so the next sweep tries again. The reverse order stranded the job: a pending
		// state in :processing is skipped by the status check above, forever. If the state
		// write fails after the move the job is already requeued and the worker that pops it
		// does not look at the status; only the retry_count increment is lost.
		if err := moveFromProcessing(ctx, videoID, cfg.ProcessingRequestQueue); err != nil {
			log.Warn().Err(err).Str("videoID", videoID).Msg("Failed to re-queue orphan job")
			continue
		}
		state.Status = JobStatusPending
		if err := setJobState(ctx, videoID, *state); err != nil {
			log.Warn().Err(err).Str("videoID", videoID).Msg("Failed to update state after re-queuing orphan job")
		}
	}
}

// deadLetterOrphanWithoutState handles a job parked in :processing whose job:<id> expired or was
// deleted. Without the state there is no callback_url and no age to judge it by, so requeueing
// would only make the worker dead-letter it on the next pop (same outcome as the worker's
// deadLetterJobWithoutState): do it now instead of leaving it in :processing forever.
func deadLetterOrphanWithoutState(ctx context.Context, videoID string) {
	log.Error().Str("videoID", videoID).Msg("Orphan job has no state (expired or deleted), moving to dead letter queue")
	if err := moveFromProcessing(ctx, videoID, deadLetterQueueName()); err != nil {
		log.Warn().Err(err).Str("videoID", videoID).Msg("Failed to move stateless orphan job to dead letter queue")
	}
}

// moveFromProcessing takes the job out of :processing and pushes it to destination in one
// MULTI/EXEC, so a failure between the two can never drop the job.
func moveFromProcessing(ctx context.Context, videoID, destination string) error {
	_, err := client.TxPipelined(ctx, func(pipe redis.Pipeliner) error {
		pipe.LRem(ctx, processingQueueName(), 1, videoID)
		pipe.LPush(ctx, destination, videoID)
		return nil
	})
	return err
}

// GetQueueSize returns the number of jobs waiting in the request queue.
func GetQueueSize(ctx context.Context) (int64, error) {
	return client.LLen(ctx, cfg.ProcessingRequestQueue).Result()
}

// HealthCheck checks whether the Redis client is healthy.
func HealthCheck(ctx context.Context) error {
	return client.Ping(ctx).Err()
}
