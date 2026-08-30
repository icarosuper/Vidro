package circuitbreaker

import (
	"errors"
	"testing"
	"time"

	"github.com/sony/gobreaker"
)

var errFailure = errors.New("simulated failure")

func TestMinIO_InitialState_Closed(t *testing.T) {
	calls := 0
	_, err := MinIO.Execute(func() (interface{}, error) {
		calls++
		return nil, nil
	})
	if err != nil {
		t.Fatalf("MinIO circuit should be closed, but returned error: %v", err)
	}
	if calls != 1 {
		t.Fatalf("expected 1 call, got %d", calls)
	}
}

func TestRedis_InitialState_Closed(t *testing.T) {
	calls := 0
	_, err := Redis.Execute(func() (interface{}, error) {
		calls++
		return nil, nil
	})
	if err != nil {
		t.Fatalf("Redis circuit should be closed, but returned error: %v", err)
	}
	if calls != 1 {
		t.Fatalf("expected 1 call, got %d", calls)
	}
}

func TestCircuitBreaker_OpensAfter5ConsecutiveFailures(t *testing.T) {
	cb := gobreaker.NewCircuitBreaker(gobreaker.Settings{
		Name:        "test-minio",
		MaxRequests: 1,
		Interval:    30 * time.Second,
		Timeout:     60 * time.Second,
		ReadyToTrip: func(counts gobreaker.Counts) bool {
			return counts.ConsecutiveFailures >= 5
		},
	})

	for i := 0; i < 5; i++ {
		cb.Execute(func() (interface{}, error) { //nolint:errcheck
			return nil, errFailure
		})
	}

	if cb.State() != gobreaker.StateOpen {
		t.Fatalf("circuit should be open after 5 failures, state: %s", cb.State())
	}
}

func TestCircuitBreaker_OpensAfter3ConsecutiveFailures_Redis(t *testing.T) {
	cb := gobreaker.NewCircuitBreaker(gobreaker.Settings{
		Name:        "test-redis",
		MaxRequests: 1,
		Interval:    30 * time.Second,
		Timeout:     30 * time.Second,
		ReadyToTrip: func(counts gobreaker.Counts) bool {
			return counts.ConsecutiveFailures >= 3
		},
	})

	for i := 0; i < 3; i++ {
		cb.Execute(func() (interface{}, error) { //nolint:errcheck
			return nil, errFailure
		})
	}

	if cb.State() != gobreaker.StateOpen {
		t.Fatalf("Redis circuit should be open after 3 failures, state: %s", cb.State())
	}
}

func TestCircuitBreaker_RejectsCallsWhenOpen(t *testing.T) {
	cb := gobreaker.NewCircuitBreaker(gobreaker.Settings{
		Name:        "test-open",
		MaxRequests: 1,
		Interval:    30 * time.Second,
		Timeout:     60 * time.Second,
		ReadyToTrip: func(counts gobreaker.Counts) bool {
			return counts.ConsecutiveFailures >= 3
		},
	})

	for i := 0; i < 3; i++ {
		cb.Execute(func() (interface{}, error) { //nolint:errcheck
			return nil, errFailure
		})
	}

	_, err := cb.Execute(func() (interface{}, error) {
		return nil, nil
	})

	if !errors.Is(err, gobreaker.ErrOpenState) {
		t.Fatalf("expected ErrOpenState, got: %v", err)
	}
}

func TestCircuitBreaker_DoesNotOpenWithNonConsecutiveFailures(t *testing.T) {
	cb := gobreaker.NewCircuitBreaker(gobreaker.Settings{
		Name:        "test-intermittent",
		MaxRequests: 1,
		Interval:    30 * time.Second,
		Timeout:     60 * time.Second,
		ReadyToTrip: func(counts gobreaker.Counts) bool {
			return counts.ConsecutiveFailures >= 5
		},
	})

	// 4 failures interleaved with 1 success — should not open
	for i := 0; i < 4; i++ {
		cb.Execute(func() (interface{}, error) { //nolint:errcheck
			return nil, errFailure
		})
		cb.Execute(func() (interface{}, error) { //nolint:errcheck
			return "ok", nil
		})
	}

	if cb.State() != gobreaker.StateClosed {
		t.Fatalf("circuit should not open with non-consecutive failures, state: %s", cb.State())
	}
}

func TestCircuitBreaker_ReturnsResultWhenClosed(t *testing.T) {
	cb := gobreaker.NewCircuitBreaker(gobreaker.Settings{
		Name: "test-result",
	})

	result, err := cb.Execute(func() (interface{}, error) {
		return "expected-value", nil
	})

	if err != nil {
		t.Fatalf("did not expect error: %v", err)
	}
	if result != "expected-value" {
		t.Fatalf("expected 'expected-value', got: %v", result)
	}
}
