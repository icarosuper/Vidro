# 🎥 VidroProcessor

Distributed video processing worker for Vidro, built in Go.
Pulls jobs enqueued by [VidroApi](../VidroApi/) and delivers processed artifacts consumed by
[VidroFront](../VidroFront/). Stateless — scale by running more instances against the same Redis.

[![Go Version](https://img.shields.io/badge/Go-1.24-00ADD8?style=flat&logo=go)](https://golang.org/)
[![Tests](https://img.shields.io/badge/tests-passing-brightgreen)](docs/TESTING.md)
[![Coverage](https://img.shields.io/badge/coverage-63.7%25-yellow)](docs/TESTING.md)
[![Status](https://img.shields.io/badge/status-functional-brightgreen)](../TODO.md)

## ✨ Features

- ✅ **Complete processing pipeline** — 7 steps with FFmpeg
- ✅ **Distributed architecture** — concurrent workers, graceful shutdown
- ✅ **Resilience** — `BRPOPLPUSH` leases, orphan recovery, retry → dead-letter queue, circuit breakers
- ✅ **Observability** — Prometheus metrics, OpenTelemetry tracing, zerolog JSON output, `/health`
- ✅ **S3 storage** — MinIO compatible

## 🚀 Quick Start

Requires Go 1.24+, Docker, and FFmpeg locally (only to run the worker outside Docker).

```bash
cd VidroProcessor
cp .env-example .env

# dependencies come from the monorepo root stack
docker compose -f ../docker-compose.yml up -d redis minio

go run main.go
```

To run the worker in a container along with the rest of Vidro, bring up the whole stack from the
monorepo root: `cd .. && docker compose up -d --build`.

Full walkthrough — publishing a job, watching it process, inspecting artifacts:
[docs/GETTING_STARTED.md](docs/GETTING_STARTED.md).

## 📋 Processing pipeline

1. **Validation** — checks integrity with ffprobe
2. **Analysis** — extracts metadata (duration, resolution, codecs)
3. **Transcoding** — converts to MP4 (H.264 + AAC)
4. **Thumbnails** — generates 5 preview images (320x180)
5. **Audio** — extracts audio track as MP3
6. **Preview** — creates low-quality version (640px, 30s)
7. **Streaming** — segments for HLS (6s per segment)

Steps 1–3 are critical; 4–7 are not — a failure there does not fail the job. Orchestration, queue
protocol and the job state machine are in [docs/agents/architecture.md](docs/agents/architecture.md).

## 📦 Project structure

```
VidroProcessor/
├── config/                        # Configuration loading
├── internal/
│   ├── circuitbreaker/            # Redis and MinIO breakers
│   ├── processor/
│   │   ├── processor.go           # Pipeline orchestrator
│   │   └── processor-steps/       # The 7 steps
│   ├── telemetry/                 # OpenTelemetry tracing
│   └── webhook/                   # Completion callback to the API
├── metrics/                       # Prometheus metrics
├── minio/                         # MinIO client
├── queue/                         # Redis client and job state
├── test/integration/              # Testcontainers-based suite
├── docs/                          # See below
└── main.go                        # Entry point
```

## 📖 Documentation

| Doc | What it answers |
|---|---|
| [docs/agents/features-index.md](docs/agents/features-index.md) | Where does feature X live? MinIO layout, queue names |
| [docs/agents/architecture.md](docs/agents/architecture.md) | Worker lifecycle, queue protocol, pipeline, resilience |
| [docs/agents/conventions.md](docs/agents/conventions.md) | Rules for writing Go here + checklists |
| [docs/agents/design-decisions.md](docs/agents/design-decisions.md) | Why it is built this way |
| [docs/agents/config.md](docs/agents/config.md) | Every env var, its default and why |
| [docs/GETTING_STARTED.md](docs/GETTING_STARTED.md) | Local setup walkthrough end to end |
| [docs/OBSERVABILITY.md](docs/OBSERVABILITY.md) | Endpoints, metrics, Grafana, alerts |
| [docs/TESTING.md](docs/TESTING.md) | Test layout, coverage, how to run |
| [../docs/troubleshooting-stuck-video.md](../docs/troubleshooting-stuck-video.md) | A video that never becomes `Ready` |
| [../TODO.md](../TODO.md) | Backlog of all three services — **P6** is this worker |

## 🤝 Contributing

Vidro is a monorepo; the rules for all three services are in the root
[CLAUDE.md](../CLAUDE.md). Short version: conventional commits **in Portuguese**, subject line only,
straight to `master` for small changes — a branch only for a large feature, and by agreement.

## 📄 License

This project is provided as-is, without warranties.

## 🙏 Acknowledgements

- [FFmpeg](https://ffmpeg.org/) — video processing
- [Zerolog](https://github.com/rs/zerolog) — structured logging
- [Prometheus](https://prometheus.io/) — metrics
- [MinIO](https://min.io/) — object storage

---

**Status**: 🚀 Functional — no release tagged yet; see [../TODO.md](../TODO.md) for what is next
