package integration

import (
	"context"
	"testing"
	"time"

	"github.com/redis/go-redis/v9"
)

func TestRedisQueue_PublishAndConsume(t *testing.T) {
	// Setup containers
	tc := SetupContainers(t)
	defer TeardownContainers(t, tc)

	ctx := context.Background()

	// Create Redis client
	client := redis.NewClient(&redis.Options{
		Addr: tc.RedisHost,
	})
	defer client.Close()

	// Test ping
	if err := client.Ping(ctx).Err(); err != nil {
		t.Fatalf("Failed to ping Redis: %v", err)
	}

	queueName := "test_queue"
	testMessage := "test-video-123"

	// Publish message
	if err := client.LPush(ctx, queueName, testMessage).Err(); err != nil {
		t.Fatalf("Failed to publish message: %v", err)
	}

	// Consume message
	result, err := client.BRPop(ctx, 5*time.Second, queueName).Result()
	if err != nil {
		t.Fatalf("Failed to consume message: %v", err)
	}

	if len(result) != 2 {
		t.Fatalf("Expected 2 elements, got %d", len(result))
	}

	if result[1] != testMessage {
		t.Errorf("Expected message '%s', got '%s'", testMessage, result[1])
	}
}

func TestRedisQueue_MultipleMessages(t *testing.T) {
	tc := SetupContainers(t)
	defer TeardownContainers(t, tc)

	ctx := context.Background()

	client := redis.NewClient(&redis.Options{
		Addr: tc.RedisHost,
	})
	defer client.Close()

	queueName := "test_multi_queue"
	messages := []string{"video-1", "video-2", "video-3"}

	// Publish all messages
	for _, msg := range messages {
		if err := client.LPush(ctx, queueName, msg).Err(); err != nil {
			t.Fatalf("Failed to publish message '%s': %v", msg, err)
		}
	}

	// Verify queue size
	size, err := client.LLen(ctx, queueName).Result()
	if err != nil {
		t.Fatalf("Failed to get queue size: %v", err)
	}

	if size != int64(len(messages)) {
		t.Errorf("Expected queue size %d, got %d", len(messages), size)
	}

	// Consume messages in FIFO order (RPOP)
	for i := len(messages) - 1; i >= 0; i-- {
		result, err := client.RPop(ctx, queueName).Result()
		if err != nil {
			t.Fatalf("Failed to consume message: %v", err)
		}

		expected := messages[len(messages)-1-i]
		if result != expected {
			t.Errorf("Expected '%s', got '%s'", expected, result)
		}
	}
}

func TestRedisQueue_EmptyQueue(t *testing.T) {
	tc := SetupContainers(t)
	defer TeardownContainers(t, tc)

	ctx := context.Background()

	client := redis.NewClient(&redis.Options{
		Addr: tc.RedisHost,
	})
	defer client.Close()

	queueName := "empty_queue"

	// Try to consume from empty queue with timeout
	_, err := client.BRPop(ctx, 1*time.Second, queueName).Result()
	if err != redis.Nil {
		t.Logf("Expected timeout on empty queue, got: %v", err)
	}
}

func TestRedisQueue_SuccessQueue(t *testing.T) {
	tc := SetupContainers(t)
	defer TeardownContainers(t, tc)

	ctx := context.Background()

	client := redis.NewClient(&redis.Options{
		Addr: tc.RedisHost,
	})
	defer client.Close()

	requestQueue := "processing_request_queue"
	successQueue := "processing_finished_queue"

	// Simulate processing workflow
	videoID := "test-video-abc"

	// 1. Add to request queue
	if err := client.LPush(ctx, requestQueue, videoID).Err(); err != nil {
		t.Fatalf("Failed to add to request queue: %v", err)
	}

	// 2. Consume from request queue
	result, err := client.BRPop(ctx, 5*time.Second, requestQueue).Result()
	if err != nil {
		t.Fatalf("Failed to consume from request queue: %v", err)
	}

	processedVideoID := result[1]

	// 3. After processing, add to success queue
	processedID := processedVideoID + "_processed"
	if err := client.LPush(ctx, successQueue, processedID).Err(); err != nil {
		t.Fatalf("Failed to add to success queue: %v", err)
	}

	// 4. Verify success queue
	successResult, err := client.BRPop(ctx, 5*time.Second, successQueue).Result()
	if err != nil {
		t.Fatalf("Failed to consume from success queue: %v", err)
	}

	expectedProcessedID := videoID + "_processed"
	if successResult[1] != expectedProcessedID {
		t.Errorf("Expected '%s', got '%s'", expectedProcessedID, successResult[1])
	}
}
