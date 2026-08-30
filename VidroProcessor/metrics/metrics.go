package metrics

import (
	"github.com/prometheus/client_golang/prometheus"
	"github.com/prometheus/client_golang/prometheus/promauto"
)

var (
	// VideosProcessedTotal counts the total number of videos processed
	VideosProcessedTotal = promauto.NewCounterVec(
		prometheus.CounterOpts{
			Name: "videos_processed_total",
			Help: "Total number of videos processed",
		},
		[]string{"status"}, // success or error
	)

	// ProcessingDuration measures video processing time
	ProcessingDuration = promauto.NewHistogram(
		prometheus.HistogramOpts{
			Name:    "video_processing_duration_seconds",
			Help:    "Time taken to process videos in seconds",
			Buckets: prometheus.DefBuckets,
		},
	)

	// ProcessingStepDuration measures the time of each pipeline step
	ProcessingStepDuration = promauto.NewHistogramVec(
		prometheus.HistogramOpts{
			Name:    "video_processing_step_duration_seconds",
			Help:    "Time taken for each processing step in seconds",
			Buckets: []float64{0.1, 0.5, 1, 2, 5, 10, 30, 60, 120, 300},
		},
		[]string{"step"}, // validate, transcode, thumbnail, etc.
	)

	// ActiveWorkers counts the number of active workers
	ActiveWorkers = promauto.NewGauge(
		prometheus.GaugeOpts{
			Name: "active_workers",
			Help: "Number of currently active workers",
		},
	)

	// QueueSize measures the queue size
	QueueSize = promauto.NewGauge(
		prometheus.GaugeOpts{
			Name: "queue_size",
			Help: "Current size of the processing queue",
		},
	)

	// VideoSizeBytes measures the size of processed videos
	VideoSizeBytes = promauto.NewHistogram(
		prometheus.HistogramOpts{
			Name:    "video_size_bytes",
			Help:    "Size of processed videos in bytes",
			Buckets: prometheus.ExponentialBuckets(1024*1024, 2, 15), // 1MB to ~16GB
		},
	)
)
