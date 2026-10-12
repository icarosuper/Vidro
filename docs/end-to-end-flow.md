# End-to-end flow — from upload to player

The root `README.md` has the **map** (8 steps + diagram). This file has what the map cannot hold:
the shape of each payload, the retries and timeouts of each hop, and **what happens when each
handoff fails**. Each service documents its own box in `docs/agents/`; only what crosses the
boundary goes here.

Everything was checked against the code on **2026-10-05** and cites `file:line` from the root. The
worker loop is moving from `VidroProcessor/main.go` to `VidroProcessor/internal/worker/worker.go`
(P2 in `TODO.md`); for it the citations use the **function name**, which survives the move.

Video stuck halfway? The runbook is
[`troubleshooting-stuck-video.md`](troubleshooting-stuck-video.md). Following a video through the
logs: [`observability.md`](observability.md).

---

## Video states in the API

`VideoStatus` (`VidroApi/src/VidroApi.Domain/Enums/VideoStatus.cs`, values in the golden
[`contracts/enums.json`](../contracts/enums.json)): `PendingUpload` → `Processing` → `Ready` | `Failed`.

| Transition | Who does it | Where |
|---|---|---|
| — → `PendingUpload` | creating the video | `CreateVideo.cs:109` (constructor, `Video.cs:31`) |
| `PendingUpload` → `Processing` | MinIO webhook | `MinioUploadCompleted.cs:86` |
| `PendingUpload` → `Processing` | reconciliation: upload expired **with** a file | `VideoReconciliationService.cs:121` |
| `PendingUpload` → `Failed` | reconciliation: upload expired **without** a file | `VideoReconciliationService.cs:132` |
| `Processing` → `Ready` | `video-processed` webhook with `success` and `processedPath` | `VideoProcessed.cs:106-107` |
| `Processing` → `Failed` | webhook with `success: false`, or without `processedPath` | `VideoProcessed.cs:108-109` |
| `Processing` → `Failed` | reconciliation: in `Processing` for more than 90 min | `VideoReconciliationService.cs:75-87` |

The entity's `MarkAs*` methods have no guard (`Video.cs:76-92`): what prevents a wrong transition
is the handlers checking the status first. `Ready` and `Failed` are terminal — nothing in the code
leaves them.

API files cited below without a folder live in `VidroApi/src/VidroApi.Api/Features/Videos/`,
`VidroApi/src/VidroApi.Api/BackgroundServices/` or `VidroApi/src/VidroApi.Infrastructure/`.

## Numbers that govern the flow

| What | Value | Where |
|---|---|---|
| Upload URL validity (presigned PUT) | 2 h | `appsettings.json:19` (`MinIO:UploadUrlTtlHours`) |
| Reconciliation interval | 15 min | `appsettings.json:32` |
| `Processing` becomes `Failed` after | 90 min without a new `UpdatedAt` (worker worst case, 80 min, + slack) | `appsettings.json:34`, `VideoSettings.cs`; worst case in [`contracts/processing-timeout.json`](../contracts/processing-timeout.json) |
| TTL of `job:<videoId>` in Redis | 24 h | `RedisJobQueueService.cs` (`PublishJobAsync`), `jobTTL` in `VidroProcessor/queue/job.go` |
| Attempts per job in the worker | 1 + 3 retries (`MaxJobRetries`) | `MaxJobRetries` and `ShouldRetry` in `VidroProcessor/queue/job.go` |
| Job budget | 18 min (13 min of steps + 5 min of transfer) × `PROCESSING_TIMEOUT_SCALE` | `JobBudget` in `VidroProcessor/internal/processor/processor.go` |
| Orphaned lease goes back to the queue after | budget + 1 min, checked every 1 min | `OrphanThreshold` in `internal/worker/worker.go`, `RecoveryInterval` in `queue/client.go` |
| `video-processed` webhook | up to 3 attempts (network error, 5xx and 429 only; any other 4xx fails at once), 10 s timeout each, waits of 1 s and 4 s | `VidroProcessor/internal/webhook/webhook.go` (`Notify`) |
| Redis circuit breaker (worker) | opens after 3 consecutive failures, 30 s open | `internal/circuitbreaker/circuitbreaker.go:41-56` |
| MinIO circuit breaker (worker) | opens after 5 consecutive failures, 60 s open | `circuitbreaker.go:24-39` |
| Maximum video size | 5120 MB | `VidroProcessor/config/config.go:40`, checked on download (`minio/client.go`, `downloadVideo`) |
| `raw-archived/` deleted after | 30 days (bucket lifecycle) | `minio/client.go` (`rawArchivedLifecycleDays`, `configureRawArchivedLifecycle`) |
| Front polling until a terminal status | 3 s | `VidroFront/src/features/videos/hooks.ts:153-164` |

---

## 1. Front → API: create the video

- **Call:** `POST /v1/users/{username}/channels/{handle}/videos`, authenticated
  (`CreateVideo.cs:65,86`). Body: `title`, `description`, `tags`, `visibility`. From the front:
  `createVideo` in `VidroFront/src/features/videos/api.ts:82`.
- **201 response:** `videoId`, `uploadUrl` (presigned PUT to `raw/<videoId>`) and
  `uploadExpiresAt` (`CreateVideo.cs:39-44,112-124`).
- **Status:** the video is born `PendingUpload`, with `UploadExpiresAt = now + 2 h`.
- **Failure:** synchronous — the front shows the error (`UploadVideoForm.tsx:228-231`) and nothing
  was created. The URL is generated **before** `SaveChanges` (`CreateVideo.cs:114-117`); if the save
  fails, all that is left is a signed URL for an id that does not exist, with no effect.

## 2. Browser → MinIO: presigned PUT

- **Call:** `PUT <uploadUrl>` straight to MinIO, body = the file, `Content-Type` of the file or
  `video/mp4` (`api.ts:95-130`). Bucket `videos` (compose), object `raw/<videoId>`.
- **Auth:** the URL's signature. It signs the `Host` header, which is why the API generates the
  URL for `MinIO:PublicEndpoint` (`localhost:9000`), not for the internal `minio:9000`
  (`MinioSettings.cs:21-27`, `docker-compose.yml:96-99`).
- **Limits:** the URL does not restrict size; the 5120 MB limit is only checked in the worker, on
  download.
- **Failure:** the front shows the error and stops (`UploadVideoForm.tsx:237-245`) — **no retry
  and no resume** (multipart with resume is a P5 item in `TODO.md`). The video stays
  `PendingUpload` until the reconciliation runs after `UploadExpiresAt` (2 h + up to 15 min) and
  marks it `Failed`.

## 3. MinIO → API: `POST /webhooks/minio-upload-completed`

- **Configuration:** not in code — the compose's `minio-init` registers the target and the event:
  `notify_webhook:vidroapi` → `http://api:5000/webhooks/minio-upload-completed`, `auth_token`,
  `put` event with the `raw/` prefix (`docker-compose.yml:73-79`). Outside the root compose,
  nobody configures this.
- **Auth:** `Authorization: Bearer <Webhook:MinioUploadToken>`, string comparison; wrong → 401
  (`MinioUploadCompleted.cs:37-41`).
- **Payload read:** only `EventName` and `Key` from the MinIO event
  (`MinioUploadCompleted.cs:17-21`). An `EventName` other than `s3:ObjectCreated:Put` → 200 and
  ignored (`:43-45`). `Key` comes as `videos/raw/<videoId>`; the id is the segment after `raw`
  (`ParseVideoIdFromKey`, `:61-71`) — not parsed → 200 and ignored.
- **Correlation id:** MinIO does not send `X-Correlation-ID`, so the id the middleware
  **generates** for this request is the one that follows the job to the worker
  (`MinioUploadCompleted.cs:51-53`). The id of the browser request that created the video is
  **not** the same; the two are joined by `videoId`.
- **Idempotency:** the handler only acts on `PendingUpload` (`:82-84`). A repeated event, a missing
  video or one already in another state → the handler returns a domain error, but the endpoint
  ignores the result and answers **200** (`:55-58`) — MinIO does not redeliver.
- **Failure:**
  - API down or 5xx: delivery is up to MinIO; the compose does not configure `queue_dir`
    (`docker-compose.yml:73-75`), so there is no persistent event queue on the MinIO side.
    The safety net is reconciliation: after `UploadExpiresAt`, if `raw/<videoId>` exists, it
    publishes the job and marks `Processing` with a new, logged correlation id
    (`VideoReconciliationService.cs:104-125`). Cost: up to 2 h + 15 min of delay.
  - Wrong token: 401 on every event, silent for the user; same reconciliation path.

## 4. API → Redis: publish the job

Inside the same handler (`MinioUploadCompleted.cs`, `Handle`), in a transaction, in this order:
`MarkAsProcessing` (in memory) → `SaveChanges` → `PublishJobAsync` → `Commit`
([`design-decisions.md #14`](../VidroApi/docs/agents/design-decisions.md#14-the-job-is-published-inside-the-transaction-that-marks-the-video-processing)).

- **Keys** (`RedisJobQueueService.cs`, `JobStateKey`/`SerializeJobState`):
  - `SET job:<videoId> <json> EX 86400` — a **JSON string**, not a hash.
  - `LPUSH video_queue <videoId>` — the queue carries only the id; everything else is in `job:`.
- **Shape of `job:<videoId>`** (snake_case; the worker deserializes it into `JobState`,
  `VidroProcessor/queue/job.go:50-63`):

  | Field | Written by | Content |
  |---|---|---|
  | `status` | API (`pending`), worker | `pending` \| `processing` \| `done` \| `failed` |
  | `callback_url` | API | `{Api:BaseUrl}/webhooks/video-processed` — `http://api:5000/...` in the compose |
  | `correlation_id` | API | id of the step 3 request (or generated by reconciliation) |
  | `retry_count` | API (`0`), worker | incremented on each failure |
  | `created_at`, `updated_at` | both | Unix seconds; the worker rewrites `updated_at` on every write (`job.go:70`) |
  | `error` | worker | message of the last failure |
  | `artifacts`, `metadata` | worker | MinIO paths and `analyze` output, on `done` |

- **Queue name:** `JobQueueSettings:QueueName` in the API (`appsettings.json`) and
  `PROCESSING_REQUEST_QUEUE` in the worker (`docker-compose.yml`, `.env-example`) — two independent
  settings, both checked against `contracts/job-state.json`, which also pins the `job:<id>` key and
  the envelope fields above.
- **Retry:** none. Redis down → exception → the transaction rolls back, 500 to MinIO, the video
  stays `PendingUpload` and falls into the step 3 reconciliation path.
- **`SaveChanges` fails:** it fails before the publish — no job exists. *(Until 2026-10-05 the
  publish came before the save: the job ran, the step 7 webhook was ignored because the video was
  not `Processing`, and reconciliation marked it `Failed`.)*
- **The remaining failure in the middle:** `PublishJobAsync` succeeds and `Commit` fails — the old
  case, narrowed to the commit. Same outcome: processed, webhook ignored, `Failed` by
  reconciliation.

## 5. Redis → worker: consume with a lease

- **Command:** `BRPOPLPUSH video_queue video_queue:processing 0` (blocks with no timeout), inside
  the Redis circuit breaker (`VidroProcessor/queue/client.go:47-66`). The id moves atomically to the
  `video_queue:processing` lease list.
- **Start:** `processNextMessage` reads `job:` to get the `correlation_id` (missing → logs without
  the field, does not stop the job) and writes `status: processing` (`SetJobProcessing`,
  `job.go:102-113`).
- **Status in the API:** none — it stays `Processing`. The API does not know the worker picked up
  the job.
- **Worker dies midway:** the id stays in `:processing`. `recoverStuckJobs`
  (`queue/client.go:100-148`) runs every minute and returns to `video_queue` every job in
  `processing` whose `updated_at` is older than budget + 1 min, incrementing `retry_count`; once
  exhausted, it sends it to `video_queue:dead` with `error: "orphaned repeatedly..."`. In that case
  the worker also fires the failure webhook (the `onDeadLettered` callback of `StartRecovery`, which
  `main.go` wires to `Worker.NotifyDeadLettered`); if delivery fails, the API finds out through the
  90 min reconciliation. An orphan **without** `job:` has no `callback_url`, so it goes on without a
  webhook.
- **`job:` is gone** (24 h TTL, or a manual `DEL`): `SetJobProcessing` returns
  `ErrJobStateMissing` instead of recreating the state, and the worker sends the id to
  `video_queue:dead` **unprocessed**, with an `Error` log "Job state missing"
  (`deadLetterJobWithoutState`). The `callback_url` only exists in `job:`; processing would archive
  the raw without telling anyone. `raw/<videoId>` stays intact to requeue by hand (step 7 of the
  runbook). The API only finds out through the `Processing` reconciliation.
  *(Until 2026-10-05 the state was recreated without `callback_url` and the video was processed
  silently.)*

## 6. Worker ↔ MinIO: pipeline and artifacts

`processNextMessage` (`internal/worker/worker.go`), in order:

1. Downloads `raw/<videoId>` — `StatObject` + size check + `GetObject`
   (`minio/client.go`, `downloadVideo`).
2. Pipeline (`processor.ProcessVideo`): `validate` and `transcode` are **critical** (failure = the
   job fails); `analyze` is semi-critical (failure = no metadata); thumbnails, audio, preview and
   HLS are **non-critical** (failure = `Warn` log and it carries on without the artifact). Each
   step has its own timeout (`stepTimeout*` constants in `processor.go`).
3. Uploads `processed/<videoId>_processed` (critical).
4. Moves `raw/<videoId>` → `raw-archived/<videoId>` (copy + remove; a failure only logs).
5. Uploads the optional ones; an upload failure logs and drops that artifact from what the webhook
   reports.
6. Writes `status: done` with `artifacts` and `metadata`, and fires the webhook.

**Bucket layout** (`buildJobArtifacts`):

| Object | Written by | Note |
|---|---|---|
| `raw/<videoId>` | browser (step 2) | gone after step 6.4 |
| `raw-archived/<videoId>` | worker | deleted by the lifecycle after 30 days |
| `processed/<videoId>_processed` | worker | the MP4 the player plays; `Content-Type: video/mp4` |
| `thumbnails/<videoId>/thumb_00{1..5}.jpg` | worker | `processor_steps.ThumbnailCount` (5), single source for the step and the webhook |
| `audio/<videoId>.mp3` | worker | |
| `preview/<videoId>_preview.mp4` | worker | |
| `hls/<videoId>/…` | worker | `.m3u8` + `.ts`; the API does not expose it yet (P6) |

**Failure and retry** (the bookkeeping `defer` in `processNextMessage`): any critical error →
`SetJobFailed` (`retry_count++`, `error`; never recreates a missing `job:<id>`, returns
`ErrJobStateMissing`) → if `retry_count <= 3` (4 attempts in total), `RequeueJob` (`LPUSH` again,
`status: pending`; if the state is gone or cannot be read, it does not enqueue and the worker sends
the job to the DLQ); otherwise `LPUSH video_queue:dead` and the failure webhook. In every case,
`LREM` of the lease. These writes use a context that survives the job's cancellation and SIGTERM,
capped at 10 s (`bookkeepingTimeout`).

- **A permanent error goes straight to the DLQ:** if ffprobe rejects the file, validation returns
  `processor.ErrInvalidVideo` and the worker spends no retries (DLQ + failure webhook on the first
  attempt). A step timeout or a missing ffprobe do not count as permanent. The worker does not
  check the 5120 MB limit — if it is exceeded, there is no dedicated classification.
- **Job timeout** (budget exceeded): the context cancels the running FFmpeg, the step fails and it
  enters the same retry path.
- **After archiving (6.4) no step is critical:** optional ones and `SetJobDone` only log the
  failure, so the job does not go back to retry with `raw/` already moved. *(Until 2026-10-05 the
  publish to `video_success_queue` came here and was critical: failing it sent the job into retries
  that all failed on download. The queue was removed — nobody consumed it.)* One narrow case
  remains: Redis down when the job closes leaves `status: processing` and the lease in
  `:processing`; `recoverStuckJobs` requeues it and the attempts fail on download until the DLQ.
- **MinIO down:** five consecutive failures open the breaker for 60 s and jobs fail fast, spending
  retries.

## 7. Worker → API: `POST /webhooks/video-processed`

- **Payload:** the goldens in [`contracts/`](../contracts/README.md) — full success, minimal
  success, success without `processedPath`, and failure. Built by `buildWebhookPayload` from
  `job:`; the type is `webhook.Payload`, camelCase, optional fields omitted. `success` is
  `status == done`. `thumbnailPaths` are always the 5 fixed names.
- **Headers:** `Content-Type: application/json`; `X-Webhook-Signature: sha256=<hex>` — HMAC-SHA256
  of the raw body with `WEBHOOK_SECRET` (`send` in `webhook.go`; the secret is required, the
  signature is always sent); `X-Correlation-ID` with the job's `correlation_id` (`send` in
  `webhook.go`), which the API reuses in its own log.
- **Verification in the API:** HMAC of the raw body with `Webhook:Secret`, constant-time comparison
  (`VideoProcessed.cs:46-51,70-78`). Wrong or missing → 401. In the worker the secret is
  **required** (`WebhookSecret` in `VidroProcessor/config/config.go`, `notEmpty`): without it the
  worker does not start — until 2026-10-05 it was optional, and empty made every delivery get a
  401. In the compose both sides use `dev-webhook-secret` (`docker-compose.yml:130`,
  `appsettings.Development.json:54`).
- **Status in the API:** it only acts on `Processing` (`VideoProcessed.cs:89-99`).
  `success && processedPath` → `Ready`, writes `VideoArtifacts` and, if the five metadata fields
  arrive, `VideoMetadata` (`:116-147`). Anything else → `Failed`.
- **Idempotency:** as in step 3, the handler's result is ignored and the response is **200**
  (`:57-58`). A repeated webhook, or one that arrives after reconciliation already marked `Failed`,
  is dropped — **a video marked `Failed` by timeout does not go back to `Ready`**. It is no longer
  silent: the handler logs a `Warning` "Ignoring video-processed webhook" with `VideoId`, status and
  `success`.
- **Retry:** 3 attempts, 10 s timeout each, waits of 1 s and 4 s; only network errors, 5xx and 429
  are retried; any other 4xx fails at once (`Notify`). It runs in a detached goroutine, with
  `context.Background()`: it holds neither the job nor the shutdown.
- **Failure:** after the 3 attempts (or on the first, if it is a 4xx other than 429), only a
  `Warn` log "Failed to send webhook" (`notifyWebhook`) with `videoID` and `callbackURL`. The job is
  already `done` (or in the DLQ) and is not retried. The API finds out through reconciliation: 90
  min after the last `UpdatedAt`, `Failed`.
- **Single channel:** the webhook is the only way back from the worker to the API — there is no
  success queue (`video_success_queue` was removed on 2026-10-05; nothing consumed it).

## 8. API → player

- The front polls `GET /v1/videos/{videoId}` every 3 s until `Ready` or `Failed`
  (`useVideoStatus`, `hooks.ts:155-164`).
- `GetVideo` returns **presigned GET** URLs: the `ProcessedPath` MP4 valid for 4 h and the
  thumbnails for 1 h (`GetVideo.cs:65-78`, `appsettings.json:20-21`). A URL that expires mid-playback
  requires reloading the video.
- The paths come from the webhook as they are. An optional artifact whose upload failed in step 6.5
  is dropped from the result before `buildJobArtifacts`, so the webhook reports it as absent.

---

## Who fixes what

| Failure | Safety net | Time until it acts | Result |
|---|---|---|---|
| Browser PUT failed | upload reconciliation | 2 h + up to 15 min | `Failed` |
| MinIO webhook lost | upload reconciliation (finds `raw/`) | 2 h + up to 15 min | job published, carries on normally |
| Redis down on publish | upload reconciliation | 2 h + up to 15 min | job published, carries on normally |
| Critical pipeline error | worker retry | immediate, up to 4 attempts | `Ready`, or DLQ + webhook → `Failed` |
| Worker died with the job | `recoverStuckJobs` | budget + 1 min (19 min at scale 1) | retry; exhausted → DLQ + webhook → `Failed` |
| `video-processed` webhook lost | processing reconciliation | 90 min after the last `UpdatedAt` | `Failed`, even if the worker finished fine |
| `job:` expired before consumption | DLQ unprocessed, `Error` log in the worker | immediate in the worker; the API via the processing reconciliation | `Failed`, `raw/` intact to requeue |

The 90 min cover the worker's worst case with every retry: 4 attempts × 20 min (the slowest attempt
is the orphaned one — 18 min budget + 1 min until it is orphaned + 1 min until the sweep) = 80 min,
at scale 1. That number lives in
[`contracts/processing-timeout.json`](../contracts/processing-timeout.json): the worker tests that
its sum matches the golden, the API tests that its timeout stays above it. *(Until 2026-10-05 it was
45 min, calibrated for **one** attempt: a job that used its retries was marked `Failed` while the
worker was still trying.)* What the 10 min slack does not cover: waiting in the queue — with a
backlog, a job can sit in `video_queue` longer than that before it starts.

## How to observe

- **Logs:** API (Serilog → Loki) with `CorrelationId`; worker (zerolog JSON) with `videoID`,
  `workerID` and `correlationID` on every job line. The `recoverStuckJobs` and `notifyWebhook` lines
  come from `JobState.Logger(videoID)`: they have `videoID` and `correlationID` (when the job carries
  one), no `workerID`; only the job without state, which has nowhere to read the id from, logs with
  just `videoID`. The key that crosses everything — retry, DLQ, reconciliation — is `videoId`;
  details in [`observability.md`](observability.md).
- **Worker metrics** (`VidroProcessor/metrics/metrics.go`): `videos_processed_total{status}`,
  `video_processing_duration_seconds`, `video_processing_step_duration_seconds{step}`,
  `active_workers`, `queue_size` (only `video_queue`; `:processing` and `:dead` have no metric),
  `video_size_bytes`. Webhook delivery has no metric — log only.
- **Raw state:** `GET job:<videoId>` and the three lists `video_queue`, `video_queue:processing`,
  `video_queue:dead` — commands in the [runbook](troubleshooting-stuck-video.md).
