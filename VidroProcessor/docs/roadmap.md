# Roadmap - VidroProcessor

## Project Goal

Async worker: API calls → process user videos (YouTube model). Recv `videoID`, run 7-step FFmpeg pipeline, deliver artifacts → MinIO.

## Current Status: ~95% production-ready

FFmpeg pipeline works. Basic infra exists. Reliability + API integration pieces complete.

---

## ✅ Completed

- 7-step FFmpeg pipeline (validation, transcoding, thumbnails, audio, preview, HLS)
- Upload all artifacts → MinIO (thumbnails, audio, preview, HLS segments)
- Concurrent workers + graceful shutdown
- Prometheus metrics, health check, structured logging
- Unit tests (pipeline, metrics, circuitbreaker, webhook, telemetry) + integration tests (testcontainers)
- **B1**: Workers no longer blocked on shutdown — `ConsumeMessage(ctx)`
- **B2**: Artifacts steps 4–7 → MinIO
- **B3**: `docker-compose.yml` w/ all required env vars
- **B4**: MinIO password no longer logged
- **B5**: Deploy works without `.env` file
- **C1**: Job state (`queue/job.go`) — `pending → processing → done/failed` in Redis, 24h TTL; generated artifacts recorded in `done`
- **C2**: Safe consumption via `BRPOPLPUSH` — job atomically → `{queue}:processing` on consume; `AcknowledgeMessage` removes on completion; `PublishJob` creates `pending` state on producer
- **C4**: Orphan recovery — `StartRecovery` checks every min for `processing` jobs w/ old `updated_at`, re-queues them
- **P2**: Auto retry — `SetJobFailed` increments `RetryCount`; ≤3 attempts → re-queued; beyond → DLQ
- **P3**: Dead letter queue — `{queue}:dead` receives exhausted-retry jobs; state stays `failed` w/ auditable error
- **P4**: Video metadata persisted — `AnalyzeContent` returns `*VideoMetadata`; stored in `done` job state; API-accessible via `GetJobState`
- **P5**: Stricter input validation — configurable size limit via `MAX_FILE_SIZE_MB` (default 5GB); checked pre-download via `StatObject`
- **Operational metrics**: `active_workers` Inc/Dec per job; `queue_size` updated every 30s; `video_size_bytes` recorded after download
- **C3**: Webhook/callback — on completion (success or permanent failure), worker POSTs to `callbackURL` registered on job w/ full payload; optional HMAC-SHA256 via `WEBHOOK_SECRET`
- **P1**: Multi-HLS resolutions — `SegmentForStreaming` generates 240p/360p/480p/720p/1080p (≤ original only) + `master.m3u8`; `UploadDirectory` now recursive; HLS from original input (no double transcode)

---

## 🟡 Improvements — Operational quality

### Observability
- ✅ **OpenTelemetry**: per-job tracing, root span `process_job` + child spans per step (`step/validate`, `step/transcode`, etc.); OTLP via `OTEL_ENDPOINT`; auto no-op if unconfigured

### Resilience
- ✅ **Circuit breaker**: MinIO opens after 5 consecutive failures (timeout 60s); Redis after 3 (timeout 30s); state changes logged
- ✅ **Per-step timeout**: each pipeline step has own `context.WithTimeout` (validate/analyze 30s, transcode 3min, thumbnails/audio/preview 1-2min, streaming 4min)

### Configuration
- ✅ **MinIO SSL**: via `MINIO_USE_SSL` (default `false`)
- ✅ **HTTP port**: via `HTTP_PORT` (default `8080`)
- ✅ **`go-redis/v8` → `v9`**: migrated to `github.com/redis/go-redis/v9`

---

## 🟡 Recent improvements

- ✅ **Grafana Dashboard**: `grafana/provisioning/dashboards/video-processor.json` — 9 panels (workers, queue, throughput, p50/p90/p99 per step, video sizes); Prometheus + Grafana in `docker-compose`

---

## 🟡 Performance — FFmpeg pipeline speed

~500 MB / 1080p video currently = several minutes. Three changes → ~2–3 min:

### P-PERF1: HLS single-command encoding (highest impact)
`SegmentForStreaming` in `streaming.go` runs one FFmpeg process per variant sequentially — up to 5 full encode passes for 1080p, each reading entire input from scratch.

**Fix:** Replace loop w/ single FFmpeg invocation using `-filter_complex split` + multiple `-map` outputs. FFmpeg reads input once, encodes all variants simultaneously.

Expected gain: **~4–5× faster** on step 7.

### P-PERF2: Transcode preset `medium` → `fast`
`TranscodeVideo` in `transcode.go` uses `-preset medium`. For streaming/web, `fast` or `faster` ≈ 2× speed, negligible quality diff (CRF 23 unchanged).

Expected gain: **~2× faster** on step 3.

### P-PERF3: Parallelize non-critical steps 4–7
Steps 4 (thumbnails), 5 (audio), 6 (preview), 7 (streaming) in `processor.go` run sequentially but are fully independent — all read from transcoded file. Goroutines + `sync.WaitGroup` (or `errgroup`) → total time = `max(step4, step5, step6, step7)` instead of sum.

Expected gain: **~2–3× faster** for steps 4–7 combined.

### P-PERF4: Operational guard rails for optimized pipeline
Reduce rollout risk for P-PERF1/2/3:

- **Concurrency cap steps 4–7**: configurable max parallel FFmpeg tasks per job to avoid CPU/RAM spikes under load.
- **Explicit cancellation policy**: non-critical step failures don't fail pipeline; parent context cancellation still interrupts all running steps.
- **Per-step observability preserved**: keep `video_processing_step_duration_seconds{step=...}` + warning logs per parallelized step.
- **HLS fallback mode**: if single-command adaptive HLS fails, optionally fallback to sequential per-variant encoding.
- **Feature flags/env vars**: controlled rollout + fast rollback (`PARALLEL_NON_CRITICAL_STEPS`, `MAX_PARALLEL_POST_TRANSCODE_STEPS`, `HLS_SINGLE_COMMAND`, `HLS_SINGLE_COMMAND_FALLBACK`).

### P-OPT1: Optional non-critical pipeline steps
Steps 4–7 (thumbnails, audio, preview, HLS) not required for every product surface. **VidroFront** today plays the **processed MP4** + **thumbnails** — `GetVideo` builds `videoUrl` from `ProcessedPath` and never exposes `hlsPath`.

> ⚠️ **Do not read this as "HLS is dead code."** `VidroFront/src/features/videos/components/VideoPlayer.tsx` already detects `.m3u8` and plays it through `hls.js`; the player is wired, only the API field is missing. HLS is one `GetVideo` change away from being the main watch path — gate the step behind a flag, don't delete it.

**Task:** Add config (env flags or similar) to **skip** any combo of non-critical steps → reduces FFmpeg time + MinIO writes. Critical path: validate → analyze → transcode → upload generated artifacts.

**Coordination:** Align w/ **VidroApi** — webhook payload / `VideoArtifacts` must accept omitted paths where already nullable (`HlsPath`); delete/cleanup + `VideoProcessed` handler must tolerate missing optional artifacts.

---

## 🔵 Long Term — Scalability and advanced features

- **Auto-scaling**: increase workers based on queue size
- **Horizontal scaling**: multiple worker instances on different machines
- **Queue prioritization**: short videos first, long videos in separate queue

---

**Last Updated**: 2026-03-31