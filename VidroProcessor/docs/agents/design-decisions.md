# Design Decisions — VidroProcessor

Non-obvious choices shaping the worker. Read before proposing an architectural change or "fixing"
something that looks wrong — each entry explains the trade-off, so you can tell whether the
constraint still applies.

## Index

Entries are **self-contained**: read only the one you were pointed at, never the whole file. Cite a
decision by **anchor**, never by line number — `[design-decisions.md #4](design-decisions.md#4-whole-job-timeout-derived-from-the-step-timeouts)`.

- [**#1** — Redis `BRPOPLPUSH` instead of Streams / plain `BRPOP`](#1-redis-brpoplpush-instead-of-streams--plain-brpop)
- [**#2** — Retry in place, then dead-letter](#2-retry-in-place-then-dead-letter)
- [**#3** — Critical vs non-critical pipeline steps](#3-critical-vs-non-critical-pipeline-steps)
- [**#4** — Whole-job timeout derived from the step timeouts](#4-whole-job-timeout-derived-from-the-step-timeouts)
- [**#5** — HLS single-command with sequential fallback](#5-hls-single-command-with-sequential-fallback)
- [**#6** — NVENC resolved at startup, CPU fallback inside each step](#6-nvenc-resolved-at-startup-cpu-fallback-inside-each-step)
- [**#7** — Raw videos soft-archived, then deleted by lifecycle rule](#7-raw-videos-soft-archived-then-deleted-by-lifecycle-rule)
- [**#8** — Circuit breakers with different thresholds for MinIO vs Redis](#8-circuit-breakers-with-different-thresholds-for-minio-vs-redis)
- [**#9** — Worker count derived from the cores and the FFmpeg processes one job can spawn](#9-worker-count-derived-from-the-cores-and-the-ffmpeg-processes-one-job-can-spawn)
- [**#10** — Webhook contract uses camelCase to match the .NET API](#10-webhook-contract-uses-camelcase-to-match-the-net-api)
- [**#11** — Single bucket, path-based namespacing](#11-single-bucket-path-based-namespacing)
- [**#12** — Graceful shutdown with a hard 30-second ceiling](#12-graceful-shutdown-with-a-hard-30-second-ceiling)

A new entry takes the **next number** (highest today is **#12**) plus one line here in the index. Never renumber an existing entry — references elsewhere point at its anchor.

---

### 1. Redis `BRPOPLPUSH` instead of Streams / plain `BRPOP`

`queue/client.go` — `ConsumeMessage` uses `BRPOPLPUSH` to atomically move job from main queue into `:processing` sibling list.

- **Why**: need at-least-once delivery + crash recovery without message broker. `BRPOP` alone loses jobs if worker dies mid-processing; Redis Streams would work but add consumer-group state API must also understand.
- **Implication**: worker must `LREM` job from `:processing` when done (`AcknowledgeMessage`), whether succeeded, failed, or moved to DLQ. Background recovery loop re-queues anything stuck in `:processing` beyond the job budget + 1 min.
- **Trade-off**: job stuck exactly at orphan window but still running can be picked up twice. Acceptable — processing idempotent per `videoID` (uploads overwrite).

### 2. Retry in place, then dead-letter

`queue/job.go` — `MaxJobRetries = 3`; after that, `MoveToDLQ` on `<queue>:dead`.

- **Why**: transient failures (MinIO blip, FFmpeg deadlock) should self-heal, but looping forever on truly broken video wastes workers + hides bugs.
- **No automatic DLQ drain**: jobs in `:dead` need human attention. Auto-retry would mask underlying problem.
- **Retries counted in two places**: explicit `SetJobFailed` and implicit `recoverStuckJobs` (orphan recovery). Both increment `RetryCount` so repeatedly-crashing worker eventually gives up.

### 3. Critical vs non-critical pipeline steps

`internal/processor/processor.go`.

- **Why**: user's uploaded video "done" as soon as playable MP4 exists. Thumbnails, preview, audio, HLS are nice-to-haves — failing whole job on thumbnail glitch degrades product worse than silently shipping without one.
- **Consequence**: success webhook may contain empty `thumbnailPaths`, `hlsPath`, `previewPath`, or `audioPath`. API must not treat missing optional artifacts as failure.
- **Only `validate` and `transcode` are critical.** Changing classification is product-level decision — discuss with VidroApi before touching.

### 4. Whole-job timeout derived from the step timeouts

`main.go` (`jobTimeout`) plus per-step timeouts in `internal/processor/processor.go`.

- **Why**: defence in depth. Per-step timeout prevents one FFmpeg hang from monopolising worker. Whole-job timeout catches everything else (download stalls, upload stalls, recoverable bugs that never raise error).
- **Derived, not hardcoded**: `processor.JobBudget(scale)` = sum of all seven step timeouts + 5 min transfer slack (18 min at scale 1). It was a hardcoded 5 min against 13 min of steps, so any large video died mid-pipeline and went to the DLQ. Deriving it makes the invariant unbreakable — `internal/processor/timeout_test.go` asserts it.
- **Steps 4–7 run in parallel**, so the budget is a deliberate upper bound. Per-step timeouts are the real limits; this is only the backstop.
- **Tuning**: `PROCESSING_TIMEOUT_SCALE` multiplies every step timeout and the budget — the knob for slow hosts or long content. `JOB_TIMEOUT` overrides the budget outright (rarely needed). Orphan recovery threshold is derived as `jobTimeout + 1min`, so it can never requeue a job that is still running.

### 5. HLS single-command with sequential fallback

`internal/processor/processor-steps/streaming.go`.

- **Why**: single FFmpeg invocation using `-filter_complex split` + `-var_stream_map` decodes source once, emits all variants in parallel. Significantly faster than re-decoding per variant (what sequential path does).
- **But**: single command is fragile — can fail on unusual inputs (missing audio, odd codecs, filter graph edge cases). Rather than pick one path at build time, run single-command first, fall back to sequential if errors, guarded by `HLS_SINGLE_COMMAND_FALLBACK`.
- **Variant selection**: filter `hlsVariants` to those `Height <= sourceHeight` (probed via `ffprobe`) so never upscale. If probing fails, emit at least 240p variant rather than nothing.

### 6. NVENC resolved at startup, CPU fallback inside each step

`internal/processor/processor-steps/video_encoder.go`, `transcode.go`, `streaming.go`.

- **Why**: GPU availability is deploy-time fact. `ResolveVideoEncoder` probes `ffmpeg -encoders` once during `main()` so every job sees consistent choice. `VIDEO_ENCODER=auto` is default so same binary works on CPU-only hosts and GPU nodes.
- **Per-step fallback**: even after selecting NVENC, individual FFmpeg calls can fail on specific inputs (unusual colour spaces, CUDA driver hiccups). Both `transcode.go` and `streaming.go` catch those errors and retry on `libx264` to keep throughput up.
- **NVENC preset** normalized to `p1`–`p7`; invalid values silently become `p5`. Default `p5` balances quality + speed on 1080p content.

### 7. Raw videos soft-archived, then deleted by lifecycle rule

`minio/client.go` — `ArchiveRawVideo` + `configureRawArchivedLifecycle`.

- **Why**: keeping raw after processing is insurance — can reprocess if pipeline buggy, transcode params change, or user reports bad video. But keeping forever burns storage.
- **How**: on success copy `raw/<id>` → `raw-archived/<id>` and delete original. MinIO lifecycle rule (`expire-raw-archived`, 30 days) deletes archived copy automatically.
- **Why soft-move instead of single prefix**: lifecycle rule directly on `raw/` would sweep up unprocessed jobs (API uploaded, worker hasn't picked up). Explicit move defers TTL clock until processing actually done.
- **Failure is non-fatal**: if archiving fails, log warning + keep raw in place. Better to leak storage than lose source.

### 8. Circuit breakers with different thresholds for MinIO vs Redis

`internal/circuitbreaker/circuitbreaker.go`.

- **Why separate breakers**: MinIO outage shouldn't prevent Redis ops (and vice versa) — queue bookkeeping must keep running even if object storage down.
- **Different thresholds**: Redis failures cheaper to retry + more likely to self-heal, so trip faster (3 vs 5) and reset sooner (30s vs 60s). MinIO ops expensive + sometimes slow — tolerate more failures before opening to avoid thrashing.
- **`MaxRequests: 1` in half-open**: one probe request only while half-open; don't flood recovering service.

### 9. Worker count derived from the cores and the FFmpeg processes one job can spawn

`processor.DefaultWorkerCount`, called from `workerCount` in `main.go`.

- **Was `runtime.NumCPU()` until 2026-09-07**, on the premise that the worker is the unit of parallelism. It is not, and the premise was already stale: a job runs up to `MAX_PARALLEL_POST_TRANSCODE_STEPS` FFmpeg processes at once (steps 4-7), and no FFmpeg command in `internal/` passes `-threads`, so libx264 opens roughly 1.5x the core count in threads per process. `NumCPU` workers on an 8-core host meant up to **32 concurrent FFmpeg** fighting over 8 cores.
- **Why the division**: `numCPU / clampParallelSteps(...)` keeps concurrent FFmpeg near the core count. It follows the two knobs that create the contention — `PARALLEL_NON_CRITICAL_STEPS=false` or `MAX_PARALLEL_POST_TRANSCODE_STEPS=1` give one process per job, and the default goes back to one worker per core on its own. No new knob.
- **Floor of 1**: a host with fewer cores than parallel steps still starts a worker. Refusing to run is worse than oversubscribing a single job.
- **Override via `WORKER_COUNT`** (any value > 0 wins): NVENC deployments (the GPU is the bottleneck, so fewer workers is better) and containers with CPU quotas, where `NumCPU` reports the *host's* cores and the derived default is still too high.
- **`MAX_PARALLEL_POST_TRANSCODE_STEPS` is a different guard**: it bounds one job's FFmpeg processes (`clampParallelSteps`, shared with `runNonCriticalStepsParallel` so the two cannot drift). This default bounds the *host*, which nothing did before.
- **Not measured yet**: the number is reasoned, not benchmarked. P-PERF6 in the root `TODO.md` is what would confirm it — and it only became worth running once the processes stopped trampling each other.

### 10. Webhook contract uses camelCase to match the .NET API

`internal/webhook/webhook.go`.

- **Why**: VidroApi (producer) is .NET service whose JSON serialiser produces camelCase. Matching contract here avoids custom converter on API side.
- **HMAC signature is optional**: `WEBHOOK_SECRET` empty = no signing. Off in local dev, must be set in prod.
- **Delivery is background-only**: webhook failures logged but never fail job. API can always recover state from `ProcessingFinishedQueue` or by polling `job:<id>`.
- **The contract is pinned by goldens, not by prose**: `../contracts/video-processed-*.json` (monorepo root) are tested from both sides — `webhook_contract_test.go` proves `buildWebhookPayload` serialises exactly them, `VideoProcessedTests.cs` proves the API accepts exactly them. Renaming a field means editing the golden, which turns both services red in the same CI run. Both P0 bugs in `../../../TODO.md` were divergences on this boundary; the goldens are the net that would have caught them.

### 11. Single bucket, path-based namespacing

`minio/client.go`.

- **Why**: one bucket easier to provision, replicate, + secure than many. Lifecycle rules + IAM policies still scopeable by prefix (`raw-archived/`).
- **Path layout is shared contract** with VidroApi. Changing it means changing the API in the same commit.

### 12. Graceful shutdown with a hard 30-second ceiling

`main.go`.

- **Why**: on `SIGTERM` want workers to finish current job if possible to avoid leaking in-flight work to DLQ. But stuck job must not block Kubernetes pod from terminating — force-exit after 30s.
- **Tuning**: ceiling should match or undercut orchestrator's termination grace period. If you raise the whole-job budget (`PROCESSING_TIMEOUT_SCALE`/`JOB_TIMEOUT`), revisit whether 30s is still enough to drain clean-shutdown case.