# Design Decisions

Patterns that look wrong but are intentional. Read before "fixing" one of them.

## Index

Entries are **self-contained**: read only the one you were pointed at, never the whole file. Cite a
decision by **anchor**, never by line number — `[design-decisions.md #7](design-decisions.md#7-only-processedpath-is-a-required-artifact)`.

- [**#1** — Counters are denormalized and updated atomically](#1-counters-are-denormalized-and-updated-atomically)
- [**#2** — `VideoCount` on a playlist is a historical counter](#2-videocount-on-a-playlist-is-a-historical-counter)
- [**#3** — Cursor-based pagination everywhere](#3-cursor-based-pagination-everywhere)
- [**#4** — Composite indexes are declared for every common query pattern](#4-composite-indexes-are-declared-for-every-common-query-pattern)
- [**#5** — Videos belong to Channels, not Users](#5-videos-belong-to-channels-not-users)
- [**#6** — `VideoArtifacts` and `VideoMetadata` are separate tables](#6-videoartifacts-and-videometadata-are-separate-tables)
- [**#7** — Only `ProcessedPath` is a required artifact](#7-only-processedpath-is-a-required-artifact)
- [**#8** — `DeleteBehavior.Cascade` is the global default](#8-deletebehaviorcascade-is-the-global-default)
- [**#9** — MinIO cleanup goes through `PendingStorageCleanup`](#9-minio-cleanup-goes-through-pendingstoragecleanup)
- [**#10** — Single presigned PUT URL for upload](#10-single-presigned-put-url-for-upload)
- [**#11** — The OpenAPI document is generated at build and versioned](#11-the-openapi-document-is-generated-at-build-and-versioned)
- [**#12** — Metrics are the runtime's own meters, exported as-is](#12-metrics-are-the-runtimes-own-meters-exported-as-is)
- [**#13** — The correlation ID travels inside the job envelope](#13-the-correlation-id-travels-inside-the-job-envelope)

A new entry takes the **next number** (highest today is **#13**) plus one line here in the index.
Never renumber an existing entry — references elsewhere point at its anchor.

---

### 1. Counters are denormalized and updated atomically

`LikeCount`, `DislikeCount`, `ViewCount` on `Videos` and `FollowerCount` on `Channels` update atomic
via `ExecuteUpdateAsync`.

**Don't "fix" by:** computing them with `COUNT(*)`, or adding entity methods like
`IncrementLikeCount()` — an entity method is not safe under concurrency, which is the whole reason
the update is a single SQL statement. See the `ExecuteUpdateAsync` rules in
[conventions.md](conventions.md#domain-entity-conventions).

---

### 2. `VideoCount` on a playlist is a historical counter

Increment when a video is added, decrement when the **user removes** it, **never** decrement when the
video is deleted. Intentional — same behaviour as YouTube.

`DeleteBehavior.SetNull` on `PlaylistItem.VideoId` handles the FK automatically; `GetPlaylist` filters
null `VideoId` out of the response.

**Don't "fix" by:** wiring a decrement into video deletion, or recomputing the count from the items.

---

### 3. Cursor-based pagination everywhere

`CreatedAt` is the cursor. Never `OFFSET`.

---

### 4. Composite indexes are declared for every common query pattern

When a query filters on 2+ columns together (e.g. `WHERE video_id = ? AND parent_comment_id IS NULL`,
`WHERE status = ? AND visibility = ?`), add a composite `HasIndex` in the entity's
`IEntityTypeConfiguration`.

EF Core auto-creates single-column FK indexes and **never** composite ones, so the absence of one is
not a signal that it isn't needed. Declare it in the configuration file next to the rest of the
mapping, not inside a migration.

---

### 5. Videos belong to Channels, not Users

`Video.ChannelId → Channel.UserId`. A user can own multiple channels, so nothing may assume one user
= one channel. Routes follow the same shape — `/v1/users/{username}/channels/{handle}/...`, never a
flat channel route.

---

### 6. `VideoArtifacts` and `VideoMetadata` are separate tables

1:1 with `Videos`, nullable until processing completes. They are separate because they arrive later
and independently, from the worker.

---

### 7. Only `ProcessedPath` is a required artifact

The Processor reports success even when a non-critical step fails, so a success webhook may omit
`previewPath`, `hlsPath`, `audioPath`, `thumbnailPaths` (→ empty list) or the whole metadata block
(`analyze` is semi-critical).

`VideoProcessed` **must never throw** on those — a 500 there strands the video in `Processing` for
ever, and the worker does not retry a delivered webhook. A success payload without `processedPath` is
treated as a failure, not as an error.

This is the contract the worker documents on its side:
`../../../VidroProcessor/docs/agents/design-decisions.md` #3. Changing which steps are critical is a
product decision that needs both services — change them in the same commit.

**Don't "fix" by:** adding `[Required]`/non-null guards on the optional artifact fields. Diagnosing a
stuck video: `../../../docs/troubleshooting-stuck-video.md`.

---

### 8. `DeleteBehavior.Cascade` is the global default

`OnModelCreating` enforces `Cascade` on every FK. Deleting a parent auto-deletes all dependents at DB
level (PostgreSQL `ON DELETE CASCADE`), atomically — all or nothing, one transaction.

The two deliberate exceptions:

- `DeleteBehavior.Restrict` — to **block** deletion while dependents exist (shared data, peer
  relationships).
- `DeleteBehavior.SetNull` — when the child survives the parent (e.g. `PlaylistItem.VideoId`, see
  [#2](#2-videocount-on-a-playlist-is-a-historical-counter)).

---

### 9. MinIO cleanup goes through `PendingStorageCleanup`

Never call `IMinioService` directly from a delete handler. Stage `PendingStorageCleanup` records (one
per object path or prefix) **inside the same transaction** that deletes the DB rows;
`StorageCleanupService` drains the table in the background.

Use `isPrefix: true` for paths that need `DeleteObjectsByPrefixAsync` (HLS segments, thumbnails
folder).

**Why:** a direct delete cannot be rolled back with the transaction, so a failure halfway leaves
either orphaned objects or rows pointing at objects that no longer exist.

---

### 10. Single presigned PUT URL for upload

Multipart upload is planned, not implemented — it is an item in the root `TODO.md` (P5).

### 11. The OpenAPI document is generated at build and versioned

`VidroApi.Api.csproj`, `Program.cs`, `openapi/v1.json`, `.github/workflows/api.yml`.

- **Why**: `openapi/v1.json` is the URL surface VidroFront consumes. Committing it and having CI
  regenerate-and-diff turns a route change into something a reviewer sees. The P0.1 entry in
  `TODO.md` was four wrong routes in `features-index.md` that nothing could catch.
- **Behind a property** (`-p:GenerateOpenApiDocument=true`): generation boots the host, so an
  everyday `dotnet build` must not pay for it.
- **Needs `ASPNETCORE_ENVIRONMENT=Development`**: `AddInfrastructure` builds the MinIO client
  eagerly from config, and the endpoint only exists in `appsettings.Development.json`. Nothing
  connects — the endpoint just has to parse.
- **The migration opts out**: `Program.cs` skips `MigrateAsync` when the entry assembly is
  `GetDocument.Insider`. Generating the contract cannot require a live Postgres. Any future
  startup side effect has to opt out the same way.
- **Its limits are real and documented** in [`openapi/README.md`](../../openapi/README.md):
  the document describes the 43 routes and **no response schema at all**, because the handlers
  return untyped `IResult`. The six enums the front mirrors are pinned by
  `contracts/enums.json` instead, not by this file.

### 12. Metrics are the runtime's own meters, exported as-is

`Program.cs`, `src/VidroApi.Infrastructure/DependencyInjection.cs`,
`../VidroProcessor/prometheus/prometheus.yml`, `appsettings.Development.json`.

- **No hand-written counter.** .NET already emits request rate, latency and status per route
  (`Microsoft.AspNetCore.Hosting`), Kestrel connections, rate limiter queue and rejections
  (`Microsoft.AspNetCore.RateLimiting`) and the Npgsql pool. `AddMeter` + the Prometheus exporter
  publish that on `/metrics`; a custom counter only earns its place when these stop answering the
  question.
- **The exporter package is `-beta.1`, and always has been.** OpenTelemetry has never shipped a
  stable Prometheus exporter for .NET; this is the same package Microsoft's own metrics
  documentation uses. The alternative is OTLP into a collector, which the stack does not have yet
  (see `docs/observabilidade.md`, degrau 4).
- **The Npgsql data source is named `vidroapi`** so `db_client_connection_pool_name` is a fixed
  label. Npgsql's default is the connection string — password stripped, but host, port, database
  and username still land on an unauthenticated endpoint, and the value changes per environment.
- **`/metrics` is excluded from the request log** the same way `/health` is: Prometheus scrapes
  every 15s and each scrape would otherwise be a log line.
- **The endpoint is unauthenticated**, like the worker's `:8080`. It carries no user data but does
  expose the route list, so in production it belongs behind the edge, not on the public port.

### 13. The correlation ID travels inside the job envelope

`Middleware/CorrelationIdMiddleware.cs`, `Features/Videos/MinioUploadCompleted.cs`,
`BackgroundServices/VideoReconciliationService.cs`,
`src/VidroApi.Infrastructure/Services/RedisJobQueueService.cs`.

- **A queue has no headers.** HTTP propagates a correlation ID in a header and the middleware
  reuses an inbound one; Redis does not. So `PublishJobAsync` takes the ID and writes
  `correlation_id` into the job state the worker reads. This is the shared contract with
  `VidroProcessor/queue/job.go` — change one side, change the other in the same commit.
- **The ID comes from the request that enqueued the job.** For the MinIO webhook that is the ID
  this API generated (MinIO sends none). `VideoReconciliationService` has no request behind it, so
  it mints one **and logs it** — an ID nobody can read in the log joins nothing.
- **It comes back on the callback.** The worker sends `X-Correlation-ID` on
  `/webhooks/video-processed`, and the middleware reuses inbound values, so the callback logs under
  the same ID with no extra code here.
- **Not `traceparent`, on purpose.** This API has metrics but no tracing (#12), so
  `Activity.Current` is null and a `traceparent` written here would point at a trace that does not
  exist. It becomes the right field once there is a collector — `docs/observabilidade.md`, degrau 4.
