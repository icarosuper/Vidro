# Config Reference — VidroProcessor

Every runtime knob. All of them are environment variables — the worker has no config file.

Struct: `Config` in `config/config.go`, parsed by `caarlos0/env/v10`. `LoadConfig()` reads a `.env`
first (`godotenv`, missing file is fine — Docker injects the vars instead), then `env.Parse`.

- **Required** vars carry `notEmpty`: the worker **refuses to start** without them.
- **Optional** vars carry `envDefault`. No `os.Getenv` anywhere outside `config/config.go`.
- Every var here must also exist in `.env-example` — see the checklist in
  [conventions.md](conventions.md#checklist--adding-an-env-var).

## Redis — required

| Variable | Default | Description |
|---|---|---|
| `REDIS_HOST` | — **required** | `host:port`. Holds the queues **and** the job records (`job:<videoID>`). It is the only durable state the worker touches besides MinIO |
| `PROCESSING_REQUEST_QUEUE` | — **required** | Main queue. VidroApi `LPUSH`es video ids here, the worker `BRPOPLPUSH`es them out. **Shared contract** — must match the API's `JobQueueSettings:QueueName` (`video_queue` in the stack). Two sibling keys are derived from it and never configured: `<queue>:processing` (in-flight lease) and `<queue>:dead` (DLQ) — see [design-decisions.md #1](design-decisions.md#1-redis-brpoplpush-instead-of-streams--plain-brpop) |
| `PROCESSING_FINISHED_QUEUE` | — **required** | Queue the worker pushes finished job ids onto, consumed by the API. The webhook is the primary channel; this queue is the recovery path when the webhook fails ([#10](design-decisions.md#10-webhook-contract-uses-camelcase-to-match-the-net-api)) |

## MinIO — required

| Variable | Default | Description |
|---|---|---|
| `MINIO_ENDPOINT` | — **required** | `host:port`, no scheme. Inside the compose network it is `minio:9000`; the browser never talks to this endpoint (the API hands out presigned URLs on its own public endpoint) |
| `MINIO_ROOT_USER` | — **required** | Access key |
| `MINIO_ROOT_PASSWORD` | — **required** | Secret key |
| `MINIO_BUCKET_NAME` | — **required** | **One** bucket for everything; prefixes do the namespacing (`raw/`, `raw-archived/`, `processed/`, …; `minio.VideoType`). The layout is **shared contract** with VidroApi — [#11](design-decisions.md#11-single-bucket-path-based-namespacing) |
| `MINIO_USE_SSL` | `false` | `false` is right for in-cluster traffic. Set it in a deploy that crosses a network boundary |

## HTTP

| Variable | Default | Description |
|---|---|---|
| `HTTP_PORT` | `8080` | Serves `/health` and `/metrics` only. The worker has no other HTTP surface — it is a queue consumer, nothing calls into it |

## Workers

| Variable | Default | Description |
|---|---|---|
| `WORKER_COUNT` | `0` = `runtime.NumCPU()` | Goroutines consuming the queue. One per core is the right starting point because FFmpeg is CPU-bound. **Override it in two cases**: on an NVENC host (the GPU is the bottleneck, so fewer workers is faster) and in a container with a CPU quota (`NumCPU` reports the *host's* cores, not the quota, so the default over-subscribes). See [#9](design-decisions.md#9-worker-count-defaults-to-runtimenumcpu) |

## Processing

| Variable | Default | Description |
|---|---|---|
| `MAX_FILE_SIZE_MB` | `5120` (5 GB) | Ceiling on the source file. Checked in `minio.downloadVideo` by a `StatObject` **before** `GetObject`, so an oversized video is refused without ever being written to disk — not in the `validate` step, which only sees files that already downloaded |
| `PARALLEL_NON_CRITICAL_STEPS` | `true` | Run steps 4–7 (thumbnails, audio, preview, streaming) concurrently instead of one after another. Turn it **off** to make a failure reproducible — parallel runs interleave logs and make it hard to tell which step broke |
| `MAX_PARALLEL_POST_TRANSCODE_STEPS` | `4` | Cap on that concurrency. `4` is the number of steps, so the default is "no cap in practice"; lower it when several workers share one host and FFmpeg processes fight for cores |
| `HLS_SINGLE_COMMAND` | `true` | One FFmpeg invocation emitting every HLS variant (`-filter_complex split` + `-var_stream_map`), which decodes the source **once**. The sequential path re-decodes per variant and is markedly slower |
| `HLS_SINGLE_COMMAND_FALLBACK` | `true` | Fall back to the sequential path when the single command fails. The single command is fast but fragile (missing audio, odd codecs, filter-graph edge cases), so the pair is chosen at **runtime**, not at build time — [#5](design-decisions.md#5-hls-single-command-with-sequential-fallback). Turning the fallback off turns those inputs into failed jobs |
| `VIDEO_ENCODER` | `auto` | `auto` probes `ffmpeg -encoders` once at startup and picks NVENC when present; `nvenc` asks for GPU and still falls back to CPU if unavailable; `cpu` forces `libx264`. `auto` is the default so the same binary works unchanged on a CPU-only host and on a GPU node — [#6](design-decisions.md#6-nvenc-resolved-at-startup-cpu-fallback-inside-each-step). Individual FFmpeg calls fall back to `libx264` per step even after NVENC was selected |
| `NVENC_PRESET` | `p5` | FFmpeg NVENC preset, `p1`–`p7` (Turing+). Anything else is silently normalized to `p5`. `p5` is the quality/speed balance for 1080p; `p1` is fastest and visibly worse |
| `PROCESSING_TIMEOUT_SCALE` | `1` | **The calibration knob.** Multiplies every step timeout *and* the derived whole-job budget. Encoding time depends on the host CPU/GPU and on the input, and the defaults below were measured on one machine — a slow host or long content needs `2`. Symptom of it being too low: jobs dying mid-pipeline and landing in the DLQ |
| `JOB_TIMEOUT` | `0` = derived | Overrides the whole-job budget outright. Keep it `0`: `processor.JobBudget(scale)` derives it from the step timeouts, and `timeout_test.go` asserts the invariant. Hardcoding it is what caused the original bug — a 5 min budget against 13 min of steps ([#4](design-decisions.md#4-whole-job-timeout-derived-from-the-step-timeouts)). Orphan recovery uses `jobTimeout + 1min`, so a wrong value here also makes recovery requeue jobs that are still running |

### Step timeouts (constants, not env)

In `internal/processor/processor.go`. Not configurable one by one on purpose — `PROCESSING_TIMEOUT_SCALE`
multiplies all of them at once, which keeps their relative sizes intact.

| Step | Timeout | Critical? |
|---|---|---|
| `validate` | 30s | **yes** — failure aborts the job |
| `analyze` | 30s | no (semi: the API tolerates a missing metadata block) |
| `transcode` | 3min | **yes** — `processedPath` is the one required artifact |
| `thumbnails` | 60s | no |
| `audio` | 2min | no |
| `preview` | 2min | no |
| `streaming` (HLS) | 4min | no |
| *transfer slack* | 5min | covers the MinIO download of the source and the upload of every artifact, neither of which sits inside a step timeout |

`JobBudget(1)` = the sum = **18 min**. Steps 4–7 usually run in parallel, so the budget is a
deliberate upper bound — the per-step timeouts are the real limits.

## Webhook

| Variable | Default | Description |
|---|---|---|
| `WEBHOOK_SECRET` | `""` = unsigned | Signs the callback with HMAC-SHA256. Empty is fine in local dev; **must** be set in production. There is no `WEBHOOK_URL`: the callback URL arrives per job, in the queue payload, from the API's `Api:BaseUrl` |

## Observability

| Variable | Default | Description |
|---|---|---|
| `OTEL_ENDPOINT` | `""` = no-op | OTLP endpoint (e.g. `jaeger:4318`). Empty installs a no-op tracer, so tracing code stays unconditional and adds nothing when it is off |
| `OTEL_SERVICE_NAME` | `video-processor` | Service name in traces |

Prometheus metrics are always on at `HTTP_PORT` `/metrics`; they have no env var of their own. See
[OBSERVABILITY.md](../OBSERVABILITY.md).
