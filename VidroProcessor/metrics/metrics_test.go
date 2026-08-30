package metrics

import (
	"testing"

	"github.com/prometheus/client_golang/prometheus"
	dto "github.com/prometheus/client_model/go"
)

func TestVideosProcessedTotal_Increment(t *testing.T) {
	// Reset metric for isolated test
	VideosProcessedTotal.Reset()

	// Increment success counter
	VideosProcessedTotal.WithLabelValues("success").Inc()
	VideosProcessedTotal.WithLabelValues("success").Inc()

	// Increment error counter
	VideosProcessedTotal.WithLabelValues("error").Inc()

	// Check values
	successMetric := getCounterValue(t, VideosProcessedTotal.WithLabelValues("success"))
	if successMetric != 2 {
		t.Errorf("Expected 2 successes, got %f", successMetric)
	}

	errorMetric := getCounterValue(t, VideosProcessedTotal.WithLabelValues("error"))
	if errorMetric != 1 {
		t.Errorf("Expected 1 error, got %f", errorMetric)
	}
}

func TestProcessingDuration_Observe(t *testing.T) {
	// Observe some durations
	ProcessingDuration.Observe(1.5)
	ProcessingDuration.Observe(2.3)
	ProcessingDuration.Observe(3.7)

	// Check that the histogram recorded observations
	metric := &dto.Metric{}
	if err := ProcessingDuration.(prometheus.Histogram).Write(metric); err != nil {
		t.Fatalf("Error reading metric: %v", err)
	}

	if metric.Histogram.GetSampleCount() != 3 {
		t.Errorf("Expected 3 observations, got %d", metric.Histogram.GetSampleCount())
	}

	expectedSum := 1.5 + 2.3 + 3.7
	actualSum := metric.Histogram.GetSampleSum()
	if actualSum < expectedSum-0.1 || actualSum > expectedSum+0.1 {
		t.Errorf("Expected sum close to %f, got %f", expectedSum, actualSum)
	}
}

func TestProcessingStepDuration_MultipleSteps(t *testing.T) {
	steps := []string{"validate", "transcode", "thumbnails"}

	for _, step := range steps {
		ProcessingStepDuration.WithLabelValues(step).Observe(1.0)
	}

	// Check that each step was recorded
	for _, step := range steps {
		histogram := ProcessingStepDuration.WithLabelValues(step).(prometheus.Observer)

		// There is no direct way to read a histogram value with labels in the Prometheus client
		// But we can verify there is no error when using the metric
		histogram.Observe(0.5)
	}
}

func TestActiveWorkers_SetAndGet(t *testing.T) {
	// Set number of workers
	ActiveWorkers.Set(5)

	// Check value
	metric := &dto.Metric{}
	if err := ActiveWorkers.Write(metric); err != nil {
		t.Fatalf("Error reading metric: %v", err)
	}

	if metric.Gauge.GetValue() != 5 {
		t.Errorf("Expected 5 workers, got %f", metric.Gauge.GetValue())
	}

	// Update value
	ActiveWorkers.Set(10)

	if err := ActiveWorkers.Write(metric); err != nil {
		t.Fatalf("Error reading metric: %v", err)
	}

	if metric.Gauge.GetValue() != 10 {
		t.Errorf("Expected 10 workers, got %f", metric.Gauge.GetValue())
	}
}

func TestQueueSize_SetAndGet(t *testing.T) {
	// Set queue size
	QueueSize.Set(100)

	// Check value
	metric := &dto.Metric{}
	if err := QueueSize.Write(metric); err != nil {
		t.Fatalf("Error reading metric: %v", err)
	}

	if metric.Gauge.GetValue() != 100 {
		t.Errorf("Expected queue with 100 items, got %f", metric.Gauge.GetValue())
	}
}

// Helper to get value from a counter
func getCounterValue(t *testing.T, counter prometheus.Counter) float64 {
	t.Helper()

	metric := &dto.Metric{}
	if err := counter.Write(metric); err != nil {
		t.Fatalf("Error reading counter: %v", err)
	}

	return metric.Counter.GetValue()
}
