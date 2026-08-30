# Architecture — VidroProcessor

High-level shape of worker, queue protocol shared with VidroApi, pipeline orchestration model. Per-file mapping: `features-index.md`. Why things are way they are: `design-decisions.md`.

## System position

```
┌───────────┐   LPush raw/id  ┌─────────┐   BRPOPLPUSH   ┌──────────────────┐
│  VidroApi │ ───────────────▶│  Redis  │ ──────────────▶│ VidroProcessor N │
│  (.NET)   │                 │  queue  │                │    workers       │
└─────┬─────┘                 └─────────┘                └────────┬─────────┘
      │                            ▲                              │
      │                            │                              ▼
      │                            │                        ┌──────────┐
      │                            │       ArchiveRaw       │  MinIO   │
      │                            │ ◀──────────────────────┤  bucket  │
      │                            │                        └──────────┘
      │        POST webhook        │
      └────────────────────────────┘
```

Worker stateless; all durable state (queue, in-flight jobs, job metadata, artifacts) lives in Redis + MinIO. Scale horizontally: start more instances against same Redis.

## Worker lifecycle

1. `main.go` loads config, probes video encoder (NVENC vs CPU), initializes OTel, MinIO, Redis, HTTP server (`/health`, `/metrics`).
2. Spawns `WORKER_COUNT` goroutines (default `runtime.NumCPU()`). Each loops on `processNextMessage`.
3. Background goroutine (`queue.StartRecovery`) scans `:processing` queue every minute, re-queues jobs in-flight beyond the job budget + 1 min (crash recovery).
4. Another goroutine publishes `queue_size` into Prometheus every 30s.
5. On `SIGINT`/`SIGTERM`, root context cancelled; workers finish current job or killed after 30s grace.

## Queue protocol

Shared contract with VidroApi. Do not change queue names or job layout without tagging both repos together.

- **Main queue**: `ProcessingRequestQueue` (LPush by API, BRPopLPush by worker).
- **In-flight queue**: `<ProcessingRequestQueue>:processing`. Populated atomically by `BRPOPLPUSH`, acts as visibility/lease list. Workers `LREM` on completion (`AcknowledgeMessage`).
- **Dead letter queue**: `<ProcessingRequestQueue>:dead`. Jobs land here after `MaxJobRetries = 3` failed attempts.
- **Success queue**: `ProcessingFinishedQueue`. Consumed by API to react to completed jobs (plus webhook).

Each job: bare `videoID` string in queue. Full job state under Redis key `job:<videoID>` (`JobState` JSON, 24h TTL) — status, retry count, callback URL, artifacts, extracted metadata.

### Job state machine

```
         PublishJob                    processNextMessage
pending ───────────▶ pending(queued) ──────────────────▶ processing
                                                             │
                          ┌──────────────────────────────────┼──────────────────┐
                          ▼                                  ▼                  ▼
              done (with artifacts)              failed (retry < max)    failed (retry ≥ max)
                          │                                  │                  │
                     webhook success              RequeueJob → pending     MoveToDLQ + webhook failure
```

Retries counted on explicit `SetJobFailed` and implicitly by `recoverStuckJobs` (increments on orphan recovery).

## Per-job execution flow

Every job runs inside `processNextMessage` under:
- Root OTel span `process_job` (tagged `video.id`).
- 5-min hard timeout context for whole job.

Order of operations:

1. `queue.SetJobProcessing(videoID)`
2. `minio.DownloadVideo(raw, videoID, tmpInput)` — enforces `MAX_FILE_SIZE_MB`.
3. `processor.ProcessVideo(...)` — runs 7-step pipeline (see below), returns `ProcessingResult` with artifact paths in temp dir.
4. `minio.UploadVideo(tmpOutput, processed, "<id>_processed")` — primary MP4.
5. `minio.ArchiveRawVideo(videoID)` — soft delete: copy `raw/id` → `raw-archived/id`, remove original. Non-fatal.
6. Optional artifacts (thumbnails dir, audio, preview, HLS dir) uploaded if step succeeded.
7. `queue.PublishSuccessMessage(processedID)` — notifies API via finished queue.
8. `queue.SetJobDone` with artifacts + metadata.
9. `notifyWebhook` — fires only if `callbackURL` set on job state.
10. `defer`: local temp files removed; job acknowledged (`LREM` from `:processing`).

On error, `defer` increments retry count, requeues or moves to DLQ, still acknowledges (prevents double-processing). Metrics counter `videos_processed_total{status=error}` bumped at failure site.

## Processing pipeline

`internal/processor/processor.go` orchestrates 7 steps with individual timeouts and OTel spans (`runStep`):

```
┌───────────┐   ┌──────────┐   ┌───────────┐   ┌──────────────────────────────────────────┐
│ validate  │──▶│ analyze  │──▶│ transcode │──▶│ thumbnails │ audio │ preview │ streaming │
│ 30s       │   │ 30s      │   │ 3m        │   │  run in parallel (bounded), each with   │
│ critical  │   │ soft     │   │ critical  │   │  its own timeout; failures log & skip   │
└───────────┘   └──────────┘   └───────────┘   └──────────────────────────────────────────┘
```

- **Critical steps** (validate, transcode): return errors, abort pipeline.
- **Soft steps**: log warning, leave artifact path empty on failure; job still completes.
- **Analyze** semi-critical: errors logged, downstream metadata missing in webhook.
- Parallelism toggled by `PARALLEL_NON_CRITICAL_STEPS`, bounded by `MAX_PARALLEL_POST_TRANSCODE_STEPS` (clamped `[1, 4]`).
- **HLS**: single FFmpeg command with `-var_stream_map` by default; falls back to sequential per-variant on failure if `HLS_SINGLE_COMMAND_FALLBACK=true`. Variants filtered to those `<=` source height.
- **NVENC**: resolved once at startup via `ResolveVideoEncoder`. `auto` probes `ffmpeg -encoders` for `h264_nvenc`; transcode and HLS fall back to `libx264` on NVENC failure.

## Object storage layout

All under single MinIO bucket `MINIO_BUCKET_NAME`:

```
raw/<id>                             ← API upload
raw-archived/<id>                    ← after success; auto-deleted after 30 days (lifecycle rule)
processed/<id>_processed             ← MP4
thumbnails/<id>/thumb_001..005.jpg
audio/<id>.mp3
preview/<id>_preview.mp4
hls/<id>/master.m3u8
hls/<id>/<variant>/playlist.m3u8
hls/<id>/<variant>/seg_NNN.ts
```

`raw-archived/` lifecycle rule installed by `configureRawArchivedLifecycle` at startup — idempotent.

## Resilience layers

- **Circuit breakers** (`internal/circuitbreaker`) wrap every Redis and MinIO call. MinIO trips on 5 consecutive failures (60s open); Redis on 3 (30s open). State changes logged.
- **Orphan recovery** re-queues jobs stuck in `:processing` beyond the job budget + 1 min — covers worker crashes mid-job.
- **Retry + DLQ** — auto retry with state persistence, DLQ after exhaustion. DLQ jobs not auto-retried; investigate and requeue manually.
- **Per-step timeouts** prevent single bad video holding worker forever.
- **Whole-job timeout** final backstop (`processCtx`), derived from the step timeouts by `processor.JobBudget` (18 min at scale 1) — never smaller than the pipeline. Tune with `PROCESSING_TIMEOUT_SCALE`.

## Observability

- Prometheus metrics on `/metrics` (see `metrics/metrics.go`).
- OpenTelemetry traces via OTLP when `OTEL_ENDPOINT` set; else no-op.
- Structured `zerolog` logs (English only — see `conventions.md`).
- Grafana dashboards + Loki/Prometheus datasources pre-provisioned under `grafana/` for local stack.