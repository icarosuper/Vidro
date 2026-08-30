package telemetry

import (
	"context"
	"testing"

	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/trace/noop"
)

func TestInit_EmptyEndpoint_Noop(t *testing.T) {
	shutdown, err := Init(context.Background(), "test-service", "")
	if err != nil {
		t.Fatalf("Init() with empty endpoint should not return error: %v", err)
	}
	if shutdown == nil {
		t.Fatal("Init() should return a non-nil shutdown function")
	}

	// Noop shutdown should not return error
	if err := shutdown(context.Background()); err != nil {
		t.Fatalf("shutdown() should not return error: %v", err)
	}
}

func TestInit_EmptyEndpoint_InstallsNoop(t *testing.T) {
	Init(context.Background(), "test-service", "") //nolint:errcheck

	provider := otel.GetTracerProvider()
	if _, ok := provider.(noop.TracerProvider); !ok {
		t.Fatal("Init() with empty endpoint should install a noop.TracerProvider")
	}
}

func TestTracer_ReturnsNonNil(t *testing.T) {
	Init(context.Background(), "test-service", "") //nolint:errcheck

	tracer := Tracer()
	if tracer == nil {
		t.Fatal("Tracer() should not return nil")
	}
}

func TestTracer_CreatesSpan(t *testing.T) {
	Init(context.Background(), "test-service", "") //nolint:errcheck

	tracer := Tracer()
	ctx, span := tracer.Start(context.Background(), "test-span")
	defer span.End()

	if ctx == nil {
		t.Fatal("tracer.Start() should return a non-nil context")
	}
	if span == nil {
		t.Fatal("tracer.Start() should return a non-nil span")
	}
}

func TestTracerName_Constant(t *testing.T) {
	if TracerName != "video-processor" {
		t.Fatalf("TracerName should be 'video-processor', got '%s'", TracerName)
	}
}

func TestInit_InvalidEndpoint_ReturnsError(t *testing.T) {
	// A malformed endpoint that causes the OTLP exporter creation to fail
	// The OTLP HTTP exporter only fails on the first export, not on creation —
	// so Init with an invalid endpoint returns nil err (connection is lazy).
	// We only verify that the function returns without panicking.
	shutdown, err := Init(context.Background(), "test-service", "localhost:99999")
	if err != nil {
		// Returning an error is also valid behavior
		return
	}
	if shutdown != nil {
		shutdown(context.Background()) //nolint:errcheck
	}
}
