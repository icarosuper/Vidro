# Troubleshooting — a video that never becomes `Ready`

Follow this when a video sits in `Processing` (or `PendingUpload`) and never finishes, or when a job
lands in the dead-letter queue. The path crosses API, Redis, worker and MinIO, so the runbook starts
at the API and walks towards the worker — that is why it lives at the root and not inside a service.
Commands assume the root `docker-compose.yml` stack; adjust the service names if you run the pieces
by hand.

The four states a video can hold (`VidroApi.Domain.Enums.VideoStatus`): `PendingUpload` → `Processing`
→ `Ready` | `Failed`.

---

## Step 0 — Which handoff broke?

There are exactly four handoffs, and the symptom tells you which one to look at first.

| Symptom | Broken handoff | Go to |
|---|---|---|
| Stuck in `PendingUpload` | Browser → MinIO, or MinIO → API upload webhook | Step 5 |
| `Processing`, id **not** in any Redis queue, no `job:<id>` key | API → Redis (job never published) | Step 2 |
| `Processing`, id sitting in `<queue>` | Nothing consuming | Step 4 |
| `Processing`, id sitting in `<queue>:processing` | Worker holds it — running, or crashed mid-job | Step 3, then Step 4 |
| `Processing`, id in `<queue>:dead` | Worker gave up after `MaxJobRetries = 3` | Step 3 |
| `Processing`, `job:<id>` says `done` | Worker finished, the webhook never landed | Step 6 |
| Flipped to `Failed` ~45 min in, no worker error | The API's reconciliation timeout fired | Step 6 |

`<queue>` is `PROCESSING_REQUEST_QUEUE` (`video_queue` in the stack) — see [config.md](../VidroProcessor/docs/agents/config.md).

---

## Step 1 — Ask the API what it believes

```bash
curl -s localhost:5000/v1/videos/<videoId> | jq '.data | {status, createdAt, updatedAt}'
```

`updatedAt` is what the API's stuck-video reconciliation measures against, not `createdAt`. Note the
value — Step 6 needs it.

---

## Step 2 — Ask Redis what the job says

The job record is the worker's own view, independent of the API's. It has a **24 h TTL**, so a job
older than a day is simply gone; that is not evidence of anything.

```bash
docker compose exec redis redis-cli GET job:<videoId> | jq
```

```jsonc
{
  "status": "pending | processing | done | failed",
  "error": "...",            // set on failed
  "retry_count": 0,          // 3 = exhausted, moved to the DLQ
  "callback_url": "http://api:5000/webhooks/video-processed",
  "created_at": 0, "updated_at": 0
}
```

**No key at all**, and the video is younger than 24 h → the API never published the job. That is an
API-side problem: look at `MinioUploadCompleted` (`POST /webhooks/minio-upload-completed`) and at
`VideoReconciliationService`, not at the worker.

**`callback_url` empty** → the worker will process the video and notify nobody. It is filled by the
API from `Api:BaseUrl`; empty means the API published without it.

---

## Step 3 — Find the id in the queues

```bash
docker compose exec redis redis-cli LLEN video_queue
docker compose exec redis redis-cli LRANGE video_queue 0 -1
docker compose exec redis redis-cli LRANGE video_queue:processing 0 -1
docker compose exec redis redis-cli LRANGE video_queue:dead 0 -1
```

- **In `video_queue`** — nothing is consuming. Check the worker is up (`curl localhost:8080/health`)
  and that it points at the same queue name; a typo in `PROCESSING_REQUEST_QUEUE` on either side
  produces exactly this, silently.
- **In `video_queue:processing`** — a worker took the lease. Either it is genuinely running (normal
  for up to the whole-job budget, 18 min at scale 1) or it died holding it. `StartRecovery` sweeps
  every minute and re-queues anything in flight beyond `jobTimeout + 1min`, so wait that long before
  concluding anything. See [design-decisions.md #1](../VidroProcessor/docs/agents/design-decisions.md#1-redis-brpoplpush-instead-of-streams--plain-brpop).
- **In `video_queue:dead`** — three attempts failed. `job:<id>.error` holds the last one. **Nothing
  drains the DLQ automatically, on purpose** ([#2](../VidroProcessor/docs/agents/design-decisions.md#2-retry-in-place-then-dead-letter)):
  a job here is waiting for a human. Do not add an auto-drain — it would hide the bug.

---

## Step 4 — Read the worker log for that video

Every pipeline log line carries `videoID`.

```bash
docker compose logs worker | grep <videoId>
```

Read it against the pipeline: `validate → analyze → transcode → thumbnails → audio → preview → streaming`.

- **`Warn` on a step, job continues** — that is by design. `thumbnails`, `audio`, `preview` and
  `streaming` are non-critical: they log and are swallowed, and the success webhook simply omits that
  artifact ([#3](../VidroProcessor/docs/agents/design-decisions.md#3-critical-vs-non-critical-pipeline-steps)). A missing preview
  is **not** the reason the video is stuck. Do not "fix" it by making the step critical.
- **`Error` on `validate` or `transcode`** — those two are critical; the job aborts and retries.
- **`context deadline exceeded`** — a timeout. Which one matters:
  - *inside a step* → that step's timeout is too small for this host or this input. Raise
    `PROCESSING_TIMEOUT_SCALE` (the calibration knob — it multiplies every step **and** the budget),
    not the individual constant.
  - *no step reported, the job just ends* → the whole-job budget ran out, usually on the MinIO
    download or the artifact uploads, which sit outside every step timeout. Same knob.
- **No log line at all for the id** — the worker never picked it up. Back to Step 3.
- **NVENC errors followed by a `libx264` retry** — expected. The per-step CPU fallback is deliberate
  ([#6](../VidroProcessor/docs/agents/design-decisions.md#6-nvenc-resolved-at-startup-cpu-fallback-inside-each-step)).
- **`circuit breaker is open`** — MinIO or Redis was failing repeatedly and the breaker tripped. Fix
  the dependency; the breaker closes on its own (`MaxRequests: 1` probe while half-open).

---

## Step 5 — Stuck in `PendingUpload`

The video never reached the worker at all; the queue is irrelevant here.

1. Is the object in MinIO? `docker compose exec minio mc ls local/videos/raw/<videoId>` — the API
   checks exactly this in `ReconcileVideoAsync`.
2. **Object present, status still `PendingUpload`** → MinIO's `put` notification to
   `POST /webhooks/minio-upload-completed` was lost. `VideoReconciliationService` catches this within
   `ReconciliationIntervalMinutes` (15) and publishes the job itself. If it never does, check that
   the webhook's `auth_token` matches `Webhook:MinioUploadToken` on both sides.
3. **No object** → the browser's presigned PUT never completed. Once `UploadExpiresAt` passes,
   reconciliation marks the video `Failed`. Nothing to fix on the worker side.

---

## Step 6 — Worker finished but the API disagrees

`job:<id>` says `done` while the API still says `Processing` — the webhook was lost. Two safety nets
exist, and knowing which one fired tells you what actually broke:

- **Webhook delivery is fire-and-forget.** A failed POST is logged and never fails the job
  ([#10](../VidroProcessor/docs/agents/design-decisions.md#10-webhook-contract-uses-camelcase-to-match-the-net-api)). Grep the
  worker log for the videoID plus `webhook`.
- **`ProcessingFinishedQueue`** (`video_success_queue`) is the recovery channel — the API consumes it
  independently of the webhook.
- **`ReconcileStuckProcessingAsync`** marks any video `Processing` for longer than
  `VideoSettings:ProcessingTimeoutMinutes` (**45 min**) as `Failed`. That is a safety net, not a
  diagnosis: a video that flips to `Failed` at ~45 min with no error in the worker log almost always
  finished fine and lost its webhook.

**A success webhook that returns 500 is the dangerous shape.** The API must tolerate a payload with
`thumbnailPaths: []`, no `hlsPath`, no `previewPath`, no `audioPath` and no metadata block — only
`processedPath` is required. A 500 there strands the video in `Processing` and the worker will not
retry. That was BUG-1; if you are seeing it again, the bug is in `VideoProcessed.cs`, not here.

---

## Step 7 — Push it through by hand

Once the cause is understood and fixed, requeue it. `PublishJob` overwrites the job record, and
processing is idempotent per `videoID` (uploads overwrite), so replaying a job is safe.

```bash
# remove the stale lease / DLQ entry first, or recovery will fight you
docker compose exec redis redis-cli LREM video_queue:processing 0 <videoId>
docker compose exec redis redis-cli LREM video_queue:dead 0 <videoId>

# requeue with the callback the API expects
docker compose exec redis redis-cli DEL job:<videoId>
docker compose exec redis redis-cli LPUSH video_queue <videoId>
```

Pushing the raw id like this leaves `callback_url` empty, so the API is notified only through
`video_success_queue`. To exercise the webhook too, write the job record first with the same shape
`PublishJob` uses (`status: "pending"`, `callback_url: "http://api:5000/webhooks/video-processed"`).

If the raw object was already archived (`raw-archived/<id>`, 30-day lifecycle —
[#7](../VidroProcessor/docs/agents/design-decisions.md#7-raw-videos-soft-archived-then-deleted-by-lifecycle-rule)), copy it back to
`raw/<id>` before requeueing. Past 30 days the source is gone and the video cannot be reprocessed.
