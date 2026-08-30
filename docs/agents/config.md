# Config Reference

Every knob the API reads. Values live in `appsettings.json` (base) and `appsettings.Development.json`
(dev secrets and local endpoints); each section binds to a POCO in
`src/VidroApi.Infrastructure/Settings/`, registered in `src/VidroApi.Api/SettingsRegistration.cs` with
`.BindConfiguration(...)` + `.ValidateDataAnnotations().ValidateOnStart()`.

Two rules that follow from `ValidateOnStart`:

- **Defaults belong in `appsettings.json`, never in the POCO.** A code default silently masks a
  missing key; an absent key must fail at startup instead. The POCO is a typed shell — see
  [conventions.md](conventions.md#settings--poco-conventions).
- **Any key can be overridden by an env var** with `__` for `:` — `MinIO__Endpoint`,
  `ConnectionStrings__Postgres`, `Cors__AllowedOrigins__0`. That is how the root
  `docker-compose.yml` wires the stack; nothing there is special-cased in code.

## Connection strings

| Key | Dev value | Description |
|---|---|---|
| `ConnectionStrings:Postgres` | `Host=localhost;...` | Compose overrides with `Host=postgres`. Migrations run on startup in every environment |
| `ConnectionStrings:Redis` | `localhost:6379` | Same Redis the worker consumes from — the job queue, not a cache |

## `Jwt` → `JwtSettings`

| Key | Default | Description |
|---|---|---|
| `Jwt:Secret` | dev-only value in `appsettings.Development.json` | Signing key, **must** be ≥32 chars. The dev value is a placeholder and must not reach production |
| `Jwt:AccessTokenExpiryMinutes` | `15` | Short on purpose: the access token lives only in the front's memory and is renewed through the refresh cookie, so a short life costs the user nothing |
| `Jwt:RefreshTokenExpiryDays` | `7` | The refresh cookie the front sets is 30d, so the **token** is the real limit; a session older than 7 days requires signing in again |

## `MinIO` → `MinioSettings`

| Key | Default | Description |
|---|---|---|
| `MinIO:Endpoint` | `localhost:9000` (dev) | Host:port the **API** uses. Compose overrides to `minio:9000` |
| `MinIO:PublicEndpoint` | unset = same as `Endpoint` | Host:port the **browser** uses. Presigned URLs sign the `Host` header, so a URL generated for `minio:9000` fails when the browser sends `localhost:9000`. Set it whenever those two differ — which is always, under Docker |
| `MinIO:AccessKey` / `MinIO:SecretKey` | `minioadmin` (dev) | Credentials |
| `MinIO:BucketName` | `videos` | **One** bucket, prefixes namespace it. Shared contract with the worker — see the worker's [design-decisions #11](../../../VidroProcessor/docs/agents/design-decisions.md#11-single-bucket-path-based-namespacing) |
| `MinIO:UseSsl` | `false` | |
| `MinIO:UploadUrlTtlHours` | `2` | Lifetime of the presigned PUT. Also what `UploadExpiresAt` is derived from (`CreateVideo`, and the two avatar uploads), so lowering it makes reconciliation give up on stalled uploads sooner |
| `MinIO:ThumbnailUrlTtlHours` | `1` | Presigned GET for thumbnails. Shorter than the video TTL because a thumbnail URL is re-issued with the listing that shows it |
| `MinIO:VideoUrlTtlHours` | `4` | Must outlast a plausible watch session — a URL that expires mid-video breaks playback |

## `Api` → `ApiSettings`

| Key | Default | Description |
|---|---|---|
| `Api:BaseUrl` | `http://localhost:5000` | The API's own address **as the worker sees it**. It is published in the job payload as `callback_url`, so the worker POSTs its result there. Wrong value = every video stuck in `Processing` with no error anywhere. Compose sets `http://api:5000`; the dev file uses `host.docker.internal:5000` for a worker in Docker against an API on the host |

## `Cors`

| Key | Default | Description |
|---|---|---|
| `Cors:AllowedOrigins` | `["http://localhost:3000"]` | Array. The browser reaches the API on the **published** port, so the origin is the host's, not the compose service name |

## `RateLimit` → `RateLimitSettings`

| Key | Default | Description |
|---|---|---|
| `RateLimit:AuthPermitLimit` | `10` | Requests per client IP per window, on the credential endpoints only (sign in / sign up), policy `RateLimitSettings.AuthPolicy` |
| `RateLimit:AuthWindowSeconds` | `60` | 10/min is generous for a human and cheap for credential stuffing to notice |

## `VideoSettings` → `VideoSettings`

| Key | Default | Description |
|---|---|---|
| `VideoSettings:MaxTagsPerVideo` | `10` | |
| `VideoSettings:ReconciliationIntervalMinutes` | `15` | How often `VideoReconciliationService` sweeps: stale `PendingUpload` (upload expired or webhook missed) and stuck `Processing` |
| `VideoSettings:ViewDeduplicationWindowHours` | `1` | A repeat view from the same viewer inside this window doesn't count again |
| `VideoSettings:ProcessingTimeoutMinutes` | `45` | How long a video may sit in `Processing` before reconciliation marks it `Failed`. **Must stay above the worker's job budget + orphan-requeue threshold** (18 min + 19 min at `PROCESSING_TIMEOUT_SCALE=1`), otherwise a healthy job the worker is still retrying gets failed here. Raising `PROCESSING_TIMEOUT_SCALE` on the worker means raising this too |

## `TrendingSettings` → `TrendingSettings`

| Key | Default | Description |
|---|---|---|
| `TrendingSettings:ViewCountWeight` | `1.0` | |
| `TrendingSettings:LikeCountWeight` | `2.0` | A like is worth two views — it is the scarcer, more deliberate signal |
| `TrendingSettings:TimeDecayFactor` | `1.5` | Higher = newer content dominates faster |
| `TrendingSettings:WindowHours` | `48` | Only videos inside the window are ranked at all |

## `StorageCleanupSettings` → `StorageCleanupSettings`

| Key | Default | Description |
|---|---|---|
| `StorageCleanupSettings:IntervalMinutes` | `5` | How often `StorageCleanupService` drains `PendingStorageCleanup` — see [design-decisions #9](design-decisions.md#9-minio-cleanup-goes-through-pendingstoragecleanup) |
| `StorageCleanupSettings:BatchSize` | `100` | Rows per pass. Caps how long one sweep can hold MinIO calls open |

## `JobQueueSettings` → `JobQueueSettings`

| Key | Default | Description |
|---|---|---|
| `JobQueueSettings:QueueName` | `video_queue` | **Shared contract.** Must equal the worker's `PROCESSING_REQUEST_QUEUE`. A mismatch is silent on both sides: the API pushes, nothing consumes, every video stays in `Processing` |

## `Webhook` → `WebhookSettings`

| Key | Default | Description |
|---|---|---|
| `Webhook:MinioUploadToken` | `change-me-minio-upload-token` | Bearer token MinIO sends on `POST /webhooks/minio-upload-completed`. Configured on the MinIO side by the `minio-init` service in the root compose — the two must match or every upload notification is rejected and uploads only advance on the 15-minute reconciliation sweep |
| `Webhook:Secret` | dev value | Shared secret for verifying the worker's `POST /webhooks/video-processed` HMAC. Pairs with the worker's `WEBHOOK_SECRET`; empty there = unsigned requests |

## Listing caps

Each list endpoint has its own POCO so the limits can diverge without touching a shared one.

| Key | Default |
|---|---|
| `ListChannelVideosSettings:MaxLimit` | `100` |
| `ListFeedVideosSettings:MaxLimit` | `100` |
| `ListTrendingVideosSettings:MaxLimit` | `100` |
| `ListCommentsSettings:MaxLimit` | `100` |
| `ListCommentsSettings:MaxPopularLimit` | `50` |
| `ListRepliesSettings:MaxLimit` | `50` |
| `SearchSettings:MaxLimit` | `50` |
| `ChannelSettings:MaxChannelsPerUser` | `10` |

## `Serilog`

Configured entirely from `appsettings*.json`, no POCO. The dev file's `WriteTo` array is
`[0] Console, [1] GrafanaLoki` — compose overrides the Loki URI **by index**
(`Serilog__WriteTo__1__Args__uri`), so **reordering that array breaks the compose override silently**.
`/health` requests are filtered out of the logs by expression.
