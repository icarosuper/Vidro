# 🎥 VidroProcessor

Distributed video processing system for Vidro, built in Go.
Pulls jobs enqueued by [VidroApi](https://github.com/icarosuper/VidroApi) and serves processed artifacts to [VidroFront](https://github.com/icarosuper/VidroFront).
Uses a worker-based architecture with message queues.

[![Go Version](https://img.shields.io/badge/Go-1.24-00ADD8?style=flat&logo=go)](https://golang.org/)
[![Tests](https://img.shields.io/badge/tests-passing-brightgreen)](docs/TESTING.md)
[![Coverage](https://img.shields.io/badge/coverage-63.7%25-yellow)](docs/TESTING.md)
[![Status](https://img.shields.io/badge/status-functional-brightgreen)](docs/roadmap.md)

## ✨ Features

- ✅ **Complete Processing Pipeline** - 7 steps with FFmpeg
- ✅ **Distributed Architecture** - Scalable concurrent workers
- ✅ **Structured Logging** - Zerolog with JSON output
- ✅ **Prometheus Metrics** - Full observability
- ✅ **Health Checks** - Kubernetes ready
- ✅ **Async Processing** - Redis queues
- ✅ **S3 Storage** - MinIO compatible

## 🚀 Quick Start

### Prerequisites

- Go 1.24+
- Docker & Docker Compose
- FFmpeg (for local processing)

### Installation

```bash
# Clone the repository
git clone <repository-url>
cd VidroProcessor

# Copy the environment file
cp .env-example .env

# Start dependencies (Redis and MinIO)
docker-compose up -d

# Install Go dependencies
go mod download

# Build the project
go build -o video-processor

# Run
./video-processor
```

## 📋 Processing Pipeline

The system processes videos through 7 steps:

1. **Validation** - Checks integrity with ffprobe
2. **Analysis** - Extracts metadata (duration, resolution, codecs)
3. **Transcoding** - Converts to MP4 (H.264 + AAC)
4. **Thumbnails** - Generates 5 preview images (320x180)
5. **Audio** - Extracts audio track as MP3
6. **Preview** - Creates low-quality version (640px, 30s)
7. **Streaming** - Segments for HLS (6s per segment)

## 🏗️ Architecture

```
┌─────────────┐      ┌──────────┐      ┌────────────┐
│  Producer   │─────▶│  Redis   │─────▶│  Workers   │
└─────────────┘      │  Queue   │      │ (Multiple) │
                     └──────────┘      └────────────┘
                                              │
                                              ▼
                                       ┌──────────────┐
                                       │   Pipeline   │
                                       │  (7 steps)   │
                                       └──────────────┘
                                              │
                                              ▼
                                       ┌──────────────┐
                                       │    MinIO     │
                                       │  (Storage)   │
                                       └──────────────┘
```

### Components

- **Workers**: Process videos in parallel (configurable)
- **Redis**: Message queue for coordination
- **MinIO**: Object storage (S3 compatible)
- **FFmpeg**: Video processing engine
- **Prometheus**: Metrics collection
- **Grafana**: Visualization (optional)

## 📊 Observability

### HTTP Endpoints (`:8080`)

- **`/health`** - Health check (Redis + MinIO)
- **`/metrics`** - Prometheus metrics

### Available Metrics

- `videos_processed_total{status}` - Total videos processed
- `video_processing_duration_seconds` - Processing time
- `video_processing_step_duration_seconds{step}` - Time per step
- `active_workers` - Active workers
- `queue_size` - Queue size
- `video_size_bytes` - Size distribution

See [OBSERVABILITY.md](docs/OBSERVABILITY.md) for full details.

## 🧪 Tests

```bash
# Run all tests
go test ./...

# With coverage
go test ./... -cover

# Verbose output
go test -v ./...
```

**Current Coverage**: 63.7% (processor-steps)

See [TESTING.md](docs/TESTING.md) for more details.

## ⚙️ Configuration

### Environment Variables

```bash
# Redis
REDIS_HOST=localhost:6379
PROCESSING_REQUEST_QUEUE=video_queue
PROCESSING_FINISHED_QUEUE=video_success_queue

# MinIO
MINIO_ENDPOINT=localhost:9000
MINIO_ROOT_USER=minioadmin
MINIO_ROOT_PASSWORD=minioadmin
MINIO_BUCKET_NAME=videos

# Workers (optional)
WORKER_COUNT=4  # Default: number of CPUs
```

See [.env-example](./.env-example) for a complete example.

## 📦 Project Structure

```
VidroProcessor/
├── config/                 # Configuration
├── internal/
│   └── processor/
│       ├── processor.go           # Pipeline orchestrator
│       └── processor-steps/       # Processing steps
├── metrics/                # Prometheus metrics
├── minio/                  # MinIO client
├── queue/                  # Redis client
├── docs/                   # Documentation
│   ├── claude/             # Architecture, conventions, design decisions, features index
│   └── roadmap.md          # Roadmap and improvements
├── main.go                 # Entry point
├── OBSERVABILITY.md        # Observability guide
├── TESTING.md              # Testing guide
└── docker-compose.yml      # Services
```

## 🐳 Docker

### Build

```bash
docker build -t video-processor:latest .
```

### Run

```bash
docker run -d \
  --name video-processor \
  --env-file .env \
  -p 8080:8080 \
  video-processor:latest
```

### Docker Compose

```bash
# Start all services
docker-compose up -d

# View logs
docker-compose logs -f video-processor

# Stop
docker-compose down
```

## 📖 Documentation

- [🏛️ Architecture](docs/agents/architecture.md) - Worker lifecycle, queue protocol, pipeline, storage layout
- [🚀 Getting Started](docs/GETTING_STARTED.md) - Local setup walkthrough
- [🗺️ Roadmap](./docs/roadmap.md) - Features and improvements
- [📊 Observability](docs/OBSERVABILITY.md) - Metrics and monitoring
- [🧪 Testing](docs/TESTING.md) - Testing guide
- [⚙️ Config Reference](docs/agents/config.md) - Every env var, its default and why
- [🚑 Stuck video](docs/agents/troubleshooting-stuck-video.md) - Runbook for a video that never becomes Ready

## 🤝 Contributing

1. Fork the project
2. Create a branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## 📄 License

This project is provided as-is, without warranties.

## 🙏 Acknowledgements

- [FFmpeg](https://ffmpeg.org/) - Video processing
- [Zerolog](https://github.com/rs/zerolog) - Structured logging
- [Prometheus](https://prometheus.io/) - Metrics
- [MinIO](https://min.io/) - Object storage

---

**Status**: 🚀 Functional — no release tagged yet; see [roadmap.md](docs/roadmap.md) for what is next
**Last Updated**: 2026-08-29
