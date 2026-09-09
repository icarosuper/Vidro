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

A new entry takes the **next number** (highest today is **#11**) plus one line here in the index.
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

Multipart upload is planned, not implemented. See `docs/plans/` for the future work.

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

