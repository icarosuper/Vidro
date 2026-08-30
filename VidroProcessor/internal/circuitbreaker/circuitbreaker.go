// Package circuitbreaker provides pre-configured circuit breakers for the
// external services (MinIO and Redis) used by the worker.
//
// A circuit breaker monitors consecutive failures in calls to a service.
// When the threshold is reached, it "opens" and immediately rejects calls,
// preventing failure cascades. After the timeout, it allows a test call
// ("half-open"); if successful, it closes the circuit again.
package circuitbreaker

import (
	"time"

	"github.com/rs/zerolog/log"
	"github.com/sony/gobreaker"
)

// MinIO is the circuit breaker for MinIO calls (download/upload).
var MinIO *gobreaker.CircuitBreaker

// Redis is the circuit breaker for Redis calls (queue consumption, job state).
var Redis *gobreaker.CircuitBreaker

func init() {
	MinIO = gobreaker.NewCircuitBreaker(gobreaker.Settings{
		Name:        "minio",
		MaxRequests: 1,                // 1 test request in half-open state
		Interval:    30 * time.Second, // failure counting window
		Timeout:     60 * time.Second, // time in open before attempting half-open
		ReadyToTrip: func(counts gobreaker.Counts) bool {
			return counts.ConsecutiveFailures >= 5
		},
		OnStateChange: func(name string, from, to gobreaker.State) {
			log.Warn().
				Str("service", name).
				Str("from", from.String()).
				Str("to", to.String()).
				Msg("Circuit breaker state changed")
		},
	})

	Redis = gobreaker.NewCircuitBreaker(gobreaker.Settings{
		Name:        "redis",
		MaxRequests: 1,
		Interval:    30 * time.Second,
		Timeout:     30 * time.Second,
		ReadyToTrip: func(counts gobreaker.Counts) bool {
			return counts.ConsecutiveFailures >= 3
		},
		OnStateChange: func(name string, from, to gobreaker.State) {
			log.Warn().
				Str("service", name).
				Str("from", from.String()).
				Str("to", to.String()).
				Msg("Circuit breaker state changed")
		},
	})
}
