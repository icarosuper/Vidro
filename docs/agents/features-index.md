# Features index — VidroProcessor

Map features/modules → files. Update when add module, pipeline step, or external contract.

## Entry point and worker lifecycle

| Feature | File | Notes |
|---|---|---|
| Worker pool, graceful shutdown, signal handling | `main.go` | Spawns `WORKER_COUNT` workers (defaults to `runtime.NumCPU()`); 30s shutdown timeout |
| HTTP server (metrics + health) | `main.go` (`startHTTPServer`, `healthCheckHandler`) | `GET /health`, `GET /metrics` on `HTTP_PORT` |
| Per-job orchestration | `main.go` (`processNextMessage`) | Download → process → upload artifacts → publish success → webhook |
| Config loading | `config/config.go` | `caarlos0/env` + `godotenv`; required vars have `notEmpty` tag |

## Queue and job state

| Feature | File | Notes |
|---|---|---|
| Atomic queue consumption | `queue/client.go` (`ConsumeMessage`) | `BRPOPLPUSH` to a `:processing` sibling queue |
| Orphan recovery | `queue/client.go` (`StartRecovery`, `recoverStuckJobs`) | Every 1 min; re-queues jobs stuck in processing > `stuckTimeout` (`jobTimeout(cfg)+1min` from `main.go`) |
| Ack on completion | `queue/client.go` (`AcknowledgeMessage`) | Removes from `:processing` after success or DLQ |
| Success fan-out | `queue/client.go` (`PublishSuccessMessage`) | LPush to `ProcessingFinishedQueue` |
| Job state (pending → processing → done/failed) | `queue/job.go` | Stored under `job:<videoID>` in Redis, TTL 24h |
| Retry / DLQ | `queue/job.go` (`SetJobFailed`, `RequeueJob`, `MoveToDLQ`) | Up to `MaxJobRetries = 3`, then `:dead` queue |
| Job artifacts + metadata persistence | `queue/job.go` (`JobArtifacts`, `VideoMetadata`, `SetJobDone`) | Consumed by API and webhook |

Queue names (all derived from `ProcessingRequestQueue`):
- Main: `ProcessingRequestQueue`
- In-flight: `<queue>:processing`
- Dead letter: `<queue>:dead`
- Success notifications: `ProcessingFinishedQueue`

## Processing pipeline

Orchestrator: `internal/processor/processor.go` (`ProcessVideo`). 7 steps w/ individual timeouts + OTel spans via `runStep`.

| Step | File | Critical? | Timeout | Purpose |
|---|---|---|---|---|
| 1. Validate | `internal/processor/processor-steps/validate.go` | yes | 30s | `ffprobe` format/duration check; aborts if invalid |
| 2. Analyze | `internal/processor/processor-steps/analysis.go` | no | 30s | Extracts `VideoMetadata` (duration, dims, codecs, fps, bitrate) |
| 3. Transcode | `internal/processor/processor-steps/transcode.go` | yes | 3m | MP4/H.264/AAC; NVENC → CPU fallback |
| 4. Thumbnails | `internal/processor/processor-steps/thumbnail.go` | no | 60s | |
| 5. Audio extract | `internal/processor/processor-steps/audio.go` | no | 2m | MP3 |
| 6. Preview | `internal/processor/processor-steps/preview.go` | no | 2m | Short MP4 clip |
| 7. HLS segments | `internal/processor/processor-steps/streaming.go` | no | 4m | Adaptive HLS (240p–1080p), single-command w/ sequential fallback |

Support:
- `internal/processor/processor-steps/video_encoder.go` — `ResolveVideoEncoder` (probes `ffmpeg -encoders` for `h264_nvenc`) + `NormalizeNVENCPreset` (p1–p7).
- `internal/processor/processor-steps/test_helpers.go` — `GenerateTestVideo`; tests skip if `ffmpeg` missing.

Steps 4–7 run parallel by default (`runNonCriticalStepsParallel`, bounded by `MaxParallelPostTranscodeSteps`). Set `PARALLEL_NON_CRITICAL_STEPS=false` for sequential.

## Object storage (MinIO)

| Feature | File | Notes |
|---|---|---|
| Client init + bucket ensure | `minio/client.go` (`InitMinioClient`) | Creates bucket if missing |
| Download raw | `minio/client.go` (`DownloadVideo`) | Enforces `MAX_FILE_SIZE_MB` |
| Upload processed MP4 | `minio/client.go` (`UploadVideo`) | |
| Upload arbitrary artifact | `minio/client.go` (`UploadFile`, `UploadDirectory`) | Thumbnails, audio, preview, HLS tree |
| Archive raw (soft delete) | `minio/client.go` (`ArchiveRawVideo`) | Copies `raw/id` → `raw-archived/id`, removes original |
| Lifecycle rule | `minio/client.go` (`configureRawArchivedLifecycle`) | Auto-deletes `raw-archived/` after 30 days |
| Health check | `minio/client.go` (`HealthCheck`) | |

Object layout inside bucket:
- `raw/<videoID>` — uploaded by API
- `processed/<videoID>_processed` — MP4 output
- `thumbnails/<videoID>/thumb_001.jpg..005.jpg`
- `audio/<videoID>.mp3`
- `preview/<videoID>_preview.mp4`
- `hls/<videoID>/master.m3u8` + `<variant>/playlist.m3u8` + `seg_*.ts`
- `raw-archived/<videoID>` — pending lifecycle delete

## Webhook notification

| Feature | File | Notes |
|---|---|---|
| Payload contract | `internal/webhook/webhook.go` (`Payload`) | camelCase keys, matches VidroApi `VideoProcessed` |
| Delivery with retry | `internal/webhook/webhook.go` (`Notify`) | 3 attempts, exponential backoff, 10s HTTP timeout |
| HMAC signature | `internal/webhook/webhook.go` (`send`) | `X-Webhook-Signature: sha256=<hex>` when `WEBHOOK_SECRET` set |
| Caller wiring | `main.go` (`notifyWebhook`) | Fires on success + permanent DLQ failure |

## Resilience

| Feature | File | Notes |
|---|---|---|
| MinIO circuit breaker | `internal/circuitbreaker/circuitbreaker.go` | Trips after 5 consecutive failures, 60s open |
| Redis circuit breaker | `internal/circuitbreaker/circuitbreaker.go` | Trips after 3 consecutive failures, 30s open |

## Observability

| Feature | File | Notes |
|---|---|---|
| Prometheus metrics | `metrics/metrics.go` | `videos_processed_total`, `video_processing_duration_seconds`, `video_processing_step_duration_seconds`, `active_workers`, `queue_size`, `video_size_bytes` |
| OpenTelemetry tracing | `internal/telemetry/telemetry.go` | No-op when `OTEL_ENDPOINT` empty; spans `process_job` + `step/<name>` |
| Structured logs | `zerolog` everywhere | English messages only — see conventions |
| Grafana provisioning | `grafana/provisioning/` | Dashboards, Loki + Prometheus datasources |
| Promtail config | `promtail/config.yml` | Ships container logs to Loki |

## Tests

- Unit tests: co-located `*_test.go` per package.
- Integration tests: `test/integration/` — real Redis/MinIO via docker-compose. See `docs/TESTING.md`.
- FFmpeg-dependent tests auto-skip when `ffmpeg` absent (see `test_helpers.go`).