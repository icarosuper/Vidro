# TODO — Vidro (VidroApi · VidroFront · VidroProcessor)

Survey done on 2026-08-29. Each item has a file/line reference so the problem does not have to be
rediscovered.

**Closed so far:** *(2026-08-29)* BUG-1 (partial-success webhook), BUG-2 (job budget), CORS in the
API, port 5000 unified, all of P0.1 (lying docs), a single compose at the root + `/health` (the
three finally run together, E2E verified), **all of P1** (CI in the three repos, rate limiting,
migrations on startup). *(2026-08-30)* `queue/` tests in the Processor, **monorepo migration** (the
three became a single repo; see `docs/MONOREPO.md`) and the **docs consolidation** (the stuck-video
runbook moved up to the root `docs/`; the Processor's `roadmap.md` became P6 here; the Front's
`workflow.md`, the Processor's `docker-compose.yml` and the rules repeated in the three `CLAUDE.md`
files were removed; root README created).
*(2026-09-07)* search wired end to end, `ThemeToggle` in the Header, devtools out of production,
`typecheck` in the front CI (with the 23 pre-existing type errors zeroed), `gofmt` checked in the
Processor CI, the API's template test deleted, **P-PERF5** (`WORKER_COUNT` default derived from
cores ÷ FFmpeg processes per job), the **fail-fast vs. anomaly+ACK rule** written in the API's
`conventions.md`, **Biome enabled in the front CI** (lint + formatter, with the root ternary rule
relaxed to allow one line) and **`noUncheckedIndexedAccess`**. The front workflow now runs
**lint → typecheck → test → build**, all four green.
*(2026-09-09)* golden of the six enums in `contracts/enums.json` + versioned `openapi/v1.json`
(partial of the generated types item), the job's `context` crossing `queue/` and `minio/`
(`contextcheck` on), worker log in JSON filterable by `videoID`/`workerID`, `X-Correlation-ID` on
every front call, packages with high vulnerabilities updated.
*(2026-09-13)* API metrics in Prometheus, the API's correlation id reaching the worker logs, SEO
(`head` per route) + UI pinned to English, accessible names on icon-only controls, load test with
k6, first render tests (`CommentList`), completed phase plans deleted, and the `cancelBookkeeping`
bug in the worker fixed.
*(2026-10-05)* testable worker loop in `internal/worker` (#13 regression and crash → recovery locked
by test), `channels`/`playlists` tests in the front (with the channel avatar upload bug fixed),
`docs/end-to-end-flow.md`, and MinIO's `const token` explained. Wave B: `video_success_queue`
removed, `WEBHOOK_SECRET` required, job published only after the save, a job without state goes to
the DLQ, `Processing` timeout at 90 min locked by contract, and three front fixes (presigned PUT
checked, empty description as `null`, accessible Visibility).
Wave C: **P-OPT1** (steps 4–7 skipped via `ENABLE_*`), upload reconciliation saving before
publishing, video forms (`null` description, limit in the messages), upload abort listener, every
finding from waves B and C fixed, and **P-PERF6** (pipeline benchmark).
*(2026-10-06, later)* **P-PERF6, second half**: `WORKER_COUNT` varied — knee of the curve at 2
workers, which `DefaultWorkerCount` already gives. *(2026-10-08)* **P-PERF6 closed** with a real
video (55 s 1080p HEVC, 100 MB): ~26 s per job, flat curve from 1 to 8 workers, and the "~2–3 min"
for 500 MB becomes plausible.
*(2026-10-06)* **the rest of "Flow reliability"**: invalid video straight to the DLQ without
retries, an exhausted orphan notifies the API by webhook, `Run` pausing after a queue read failure,
webhook without an optional artifact that failed to upload, `correlationID` in the recovery/webhook
logs, and the atomic `LREM` of `moveFromProcessing`.
*(2026-10-12)* **`contracts/job-state.json`**: queue name, `job:<id>` key and job envelope locked by
golden on both sides (API and worker), mutation-verified.
Items marked `[x]` carry the commit and what was left in place.

**Overall state:** the features are done (every Front phase ✅, API plan 100% checked, Processor
~95%). What is missing is not features: (a) test gaps in the reliability logic (the worker loop,
the E2E), (b) pipeline performance (P6), (c) the end-to-end flow doc and (d) product (P5).
~~The three never running together~~ and ~~docs that lie~~ are resolved.

**Maintaining this file:** an item closed → strike it in the body **and** update "Closed so far"
above and the "Suggested order" at the end, in the same commit. See the root `CLAUDE.md`.

---

## P0 — Contract bugs between repos (found in this analysis, not recorded anywhere)

### ~~BUG-1~~ ✅ — `video-processed` webhook breaks when an optional artifact is missing → video stuck in `Processing` forever

`VidroApi/src/VidroApi.Api/Features/Videos/VideoProcessed.cs:98-116`
(`PersistSuccessfulProcessing`).

The contract documented by the Processor
([`VidroProcessor/docs/agents/design-decisions.md` #3](VidroProcessor/docs/agents/design-decisions.md#3-critical-vs-non-critical-pipeline-steps))
says, verbatim:
*"success webhook may contain empty `thumbnailPaths`, `hlsPath`, `previewPath`,
or `audioPath`. API must not treat missing optional artifacts as failure."*
Only thumbnails/audio/preview/streaming are non-critical — the orchestrator swallows the error and
reports success without that path
([`conventions.md`](VidroProcessor/docs/agents/conventions.md#critical-vs-non-critical-pipeline-steps)).

The API does not honor that contract. `PersistSuccessfulProcessing` uses `!` (null-forgiving) on
every optional field, and the entity rejects the nulls:

- `VideoArtifacts` (`Domain/Entities/VideoArtifacts.cs:19-22`) does
  `ArgumentException.ThrowIfNullOrWhiteSpace` on `previewPath` and `audioPath`, and
  `ArgumentNullException.ThrowIfNull` on `thumbnailPaths`. **Only `hlsPath` is nullable.**
- EF confirms it: `VideoArtifactsConfiguration.cs` marks `preview_path` and `audio_path` as
  `IsRequired()`; only `hls_path` is `IsRequired(false)`.
- Worse in the metadata: `cmd.FileSizeBytes!.Value`, `DurationSeconds!.Value`, `Width!.Value`,
  `Height!.Value`, `Codec!` throw `InvalidOperationException` if the `analyze` step fails — and
  `analyze` is semi-critical (`VidroProcessor/docs/agents/architecture.md:93`: *"errors logged,
  downstream metadata missing in webhook"*).

**Failure chain:** a non-critical step fails (e.g. a thumbnail glitch — the very example
`design-decisions.md` uses to justify the design) → the Processor reports **success** → the API
blows up in the handler → 500 → the Processor tries 3x with backoff and gives up
(`internal/webhook/webhook.go:46-56`) → the job was already acked, so **it does not go to the DLQ**
→ the video stays in `Processing` **permanently**. And `VideoReconciliationService.cs:41` only
rescues videos in `PendingUpload` with an expired `UploadExpiresAt` — **it does not look at
`Processing`**. Nobody notices.

- [x] Make `PreviewPath` and `AudioPath` nullable in `VideoArtifacts` + EF config + migration
      (`HlsPath` already was). `ThumbnailPaths` becomes an empty list instead of null — the 5 call
      sites that do `AddRange` stay intact.
- [x] Make the `VideoMetadata` fields tolerant of a failed `analyze`.
- [x] Remove the `!` from `PersistSuccessfulProcessing` and actually handle absence.
- [x] Make `VideoReconciliationService` also rescue videos stuck in `Processing` beyond the
      expected time.
- [x] Webhook integration test with a partial-success payload (no thumbnails, no audio, no
      preview, no metadata).

> **RESOLVED** — `VidroApi`. `VideoArtifacts` now only requires `ProcessedPath`;
> `PersistSuccessfulProcessing` only writes `VideoMetadata` when the `analyze` block came whole
> (`Video.Metadata` was already an optional nav and every consumer already used `?.`).
> Success **without** `processedPath` becomes `Failed` instead of throwing — no exception path is
> left in the handler. Migration `MakeOptionalArtifactPathsNullableMigration`.
> `DeleteVideo`/`DeleteChannel` got a null guard before enqueuing the cleanup.
> Safety net: `VideoReconciliationService.ReconcileStuckProcessingAsync` marks as `Failed` a video
> in `Processing` for longer than `VideoSettings:ProcessingTimeoutMinutes` (default **45min**,
> above the Processor's 18min budget + 19min orphan requeue).
> Tests: `VideoProcessedTests` — success with no optional artifact at all → `Ready` with empty
> `thumbnailUrls`; success without `processedPath` → `Failed`. 324 tests passing.

> Note: the old `VidroProcessor/docs/roadmap.md` (P-OPT1, "Coordination" section — today P6 of
> this file) had already flagged exactly this risk — *"delete/cleanup + `VideoProcessed` handler
> must tolerate missing optional artifacts"* — and nobody acted.

### ~~BUG-2~~ ✅ — The global job timeout (5 min) was shorter than the pipeline itself (13 min)

`VidroProcessor/main.go:196` creates `processCtx` with **5 minutes**, and that budget covers the
MinIO download + the 7 steps + the upload of every artifact.

The sum of the per-step timeouts (`internal/processor/processor.go:23-29`) is **13 minutes**:
validate 30s + analyze 30s + transcode 3min + thumbnails 60s + audio 2min + preview 2min +
streaming 4min.

`docs/agents/design-decisions.md` itself defines the rule: *"whole-job budget must always exceed
sum of critical-path steps (validate + analyze + transcode) plus download + upload slack"*. The
critical path alone is 4min, leaving ~1min to download and upload everything — and P6 below says a
500 MB / 1080p video takes "several minutes" today (the 2–3 min goal is *post*-optimization).

**Consequence:** a large video blows `processCtx` midway through the pipeline → the job fails → 3
retries → DLQ. A perfectly good video never processes.

- [x] Raise the global budget, or derive it from the sum of the steps.
- [x] Expose the timeouts as env vars.

> **RESOLVED** — `VidroProcessor` `4f4a949`. `processor.JobBudget(scale)` derives the budget from
> the sum of the 7 steps + 5min of transfer slack (18min at scale 1); `main.go` uses
> `jobTimeout(cfg)`. Knobs: `PROCESSING_TIMEOUT_SCALE` (multiplies every step timeout and the
> global one) and `JOB_TIMEOUT` (overrides the global one).
> Found along the way: `StartRecovery` requeued jobs in `:processing` for >10min — with an 18min
> budget that would reprocess a **healthy** job; now the threshold is `jobTimeout + 1min`.
> Invariant locked in `internal/processor/timeout_test.go`.

---

## ~~P0.1~~ ✅ — Docs that lie (cheap, unblocks the rest)

### Docs that poison AI-assisted work

- [x] **`VidroApi/docs/agents/features-index.md`: 4 wrong routes.** Fixed to the real paths
      (`/v1/users/{username}/channels/{handle}/...` and `/v1/users/{username}/playlists`).
      I checked the whole index against the 43 `Map*` in the code: the other 39 match.
- [x] **Dead path pointers in the Front.** `../Api/` → `../VidroApi/` (3 occurrences in
      `VidroFront/CLAUDE.md`, 1 in `workflow.md`).
- [x] **`VidroFront/docs/agents/workflow.md:7`** — the forbidden path became "outside this repo
      (`VidroFront/`)", with no absolute path to rot again.
- [x] **`VidroProcessor/docs/documentation.md` deleted.** 187 lines that duplicated the README and
      `docs/agents/architecture.md` and lied in 6 places. The 3 references in the README now point
      to `architecture.md` + `GETTING_STARTED.md`.
      I checked the code before deleting: `BRPopLPush` (`queue/client.go:47`), `MINIO_USE_SSL`
      (`config/config.go:24`), a missing `.env` already tolerated (`config.go:60`,
      `errors.Is(err, os.ErrNotExist)`).
- [x] **`VidroProcessor/README.md`**: `bugs known` badge → `functional` pointing at the roadmap
      (today P6 of this file); `Version: 0.1.0` removed (there is no tag in any of the three repos,
      it was fiction) and date updated.

### False claims in the docs

- [x] `VidroApi/CLAUDE.md:77` — the automatic deploy claim became the real state: only `ci.yml`,
      only build+test, only on PRs to `master`, manual deploy. The "production ships from tags"
      line was marked as the intended strategy in both CLAUDE.md files (VidroApi and VidroFront) —
      no repo has a tag.
- [x] `VidroFront/docs/agents/workflow.md` — "Phase 6 ✅ SSR + **SEO**" → SSR only, with the note
      that only `__root.tsx` defines `head`. Bonus: phase 9 (Playlists) was missing its ✅ and is
      done (feature + route + plan with 0 pending items).
- [x] `VidroProcessor/docs/roadmap.md` (P-OPT1, today P6 here) — **the roadmap's claim was right
      and this TODO was wrong.** The watch flow does serve the MP4: `GetVideo` builds `videoUrl`
      from `ProcessedPath` and never exposes `hlsPath`. What `VideoPlayer.tsx:16` proves is that
      the *player* already plays `.m3u8` via `hls.js` — only the API field is missing. I rewrote it
      with that precision and a warning not to delete the HLS step.

## ~~P1~~ ✅ — Make the stack run together

- [x] ~~**The API has no CORS.**~~ **RESOLVED** — `VidroApi` `4b29dc6`. Default policy reading
      `Cors:AllowedOrigins` from config (`http://localhost:3000` in Development), `app.UseCors()`
      before `UseAuthentication` so the 401 also goes out with the headers. No `AllowCredentials`
      (Bearer in a header, the refresh token is a TanStack server cookie). Outside dev it closes by
      default + `Log.Warning` on startup if the list is empty.
      Tests: `tests/VidroApi.IntegrationTests/Common/CorsTests.cs`.
- [x] **API port unified at 5000** — `VidroApi` `85520e3` + `VidroProcessor` `dde09fe`.
      The front always pointed at `:5000`, but `launchSettings.json` started on `:5144`, and the
      Processor's `docker-compose.yml` sent the `minio-upload-completed` webhook to `:5144` — an
      upload would never notify the API.
- [x] **API Dockerfile** (`VidroApi/Dockerfile` + `.dockerignore`). Multi-stage sdk:10.0 →
      aspnet:10.0. The runtime image installs `libgssapi-krb5-2` (without it Npgsql logs a GSSAPI
      error on every connection) and `curl` (compose healthcheck).
- [x] **A single `docker-compose.yml` at the `vidro/` root** — postgres, redis, minio, minio-init,
      api, worker, front, loki, promtail, prometheus, grafana.
      Grafana moved to **3001**: 3000 is the front's. `minio-init` now points the webhook at
      `http://api:5000` and waits for the API to be *healthy* (MinIO validates the config by
      dialing the endpoint — before, it failed with `no such host`).
      Each repo's compose still exists for isolated work.
- [x] **`/health` in the API** — `AddHealthChecks()` + `MapHealthChecks("/health")`, liveness only
      (no dependency probe: a flaky Redis must not take the container down).
- [x] **Migrations on startup in Development** — `docker compose up` has to deliver a working API,
      and the compose's Postgres starts empty. Outside Development it remains a manual deploy step.

> **Two bugs found only because the stack really ran:**
> 1. **Presigned URL unreachable from the browser.** The API signed with `minio:9000` (internal
>    endpoint) and the browser does not resolve that host — upload and player broken.
>    Swapping the host string does not fix it: the SigV4 signature covers the `Host` header.
>    Fix: `MinIO:PublicEndpoint` + a dedicated `IMinioClient` (keyed `minio-presign`) that signs
>    for the public host. Only the two presign methods use that client; the rest stays on the
>    internal endpoint.
> 2. **Front SSR and the browser need different URLs for the API.** `api-client.ts` only used
>    `import.meta.env.VITE_API_URL`, which Vite freezes at build time (the browser's value). In the
>    container the SSR needs `http://api:5000`. It now prefers `process.env.VITE_API_URL` when
>    running on the server — the same pattern the `features/*/server.ts` files already used.
>
> Both locked by tests: `VidroApi` `tests/.../Common/MinioPresignEndpointTests.cs` (the presign
> client resolves to the public endpoint, and falls back to the internal one when there is none)
> and `VidroFront` `src/tests/api-base-url.test.ts` (on the server the runtime value beats the
> build value).
>
> **E2E verified end to end** on this stack: signup → channel → video → presigned PUT → MinIO
> webhook → Redis queue → worker (7 steps) → `video-processed` webhook → `Ready`, with `videoUrl`
> and 5 thumbnails returning **200** in the browser. It was the flow that had never run whole.
- [x] **CI in the three repos.** `.github/workflows/ci.yml` in the Front (bun install → test →
      build) and in the Processor (setup-go 1.25 → ffmpeg → build → vet → `go test ./... -cover
      -timeout 15m`). All three now trigger on **push to `master` as well as PRs** — VidroApi's
      PR-only trigger almost never ran, since the repo's convention is to commit straight to
      master. I ran both pipelines locally before committing.
      **No lint step in the Front** *(was: 109 `biome check` errors)* — resolved on 2026-09-07:
      the front workflow now runs **lint → typecheck → test → build**. See "Clean up what
      `biome check` reports" and the `tsc` item in Small mess.
      *(2026-08-30: with the monorepo, the three `ci.yml` became
      `.github/workflows/{api,front,processor}.yml` at the root, each with a `paths:` filter.)*
- [x] **Rate limiting on `SignIn`/`SignUp`** — native .NET 10 `AddRateLimiter`, zero new
      dependencies. Fixed window per IP, `auth` policy, 429 on rejection, configurable in
      `RateLimit:AuthPermitLimit` / `AuthWindowSeconds` (10/min by default), following the repo's
      validated Options pattern. Only the two credential endpoints are limited — a global limiter
      would throttle normal browsing.
      Tests: `tests/VidroApi.IntegrationTests/Auth/RateLimitTests.cs` (own factory with a budget of
      3). Also verified on the real stack: 10× 401 and then 429.
- [x] **Migrations on startup in every environment** (before, only `Development`). Deploying the
      image becomes the whole deploy. **Consequence to remember:** the schema changes before the
      old instance stops, so a migration must be backward compatible with the version still live;
      two instances starting together both try to migrate (EF Core 10 has
      `IMigrationsDatabaseLock`, so the loser waits instead of corrupting).
      README/compose docs fixed along.
- [ ] **When there is a deploy pipeline, move the migration to a release step.**
      No platform guarantees "old instance stopped before the new one starts" by default — a
      rolling deploy overlaps the two on purpose, that is how zero-downtime works.
      It can be forced (`strategy: Recreate` in k8s, `minimumHealthyPercent: 0` in ECS), but the
      price is downtime. What really solves it is a **release step** that runs the migration once,
      before any new instance takes traffic: `release` phase (Heroku), `[deploy] release_command`
      (Fly.io), Pre-Deploy Command (Render/Railway), a Helm `pre-upgrade` Job or Argo PreSync (k8s),
      or a step in the CI itself (ECS, Cloud Run, App Service — these have no native hook).
      `dotnet ef migrations bundle` generates a self-contained executable made for this.
      **Even with a release step**, a rolling deploy leaves the old code running against the new
      schema for a window → backward-compatible migrations (expand/contract) remain mandatory.
      The only escape is accepting downtime.
      While there is no pipeline (deploy is manual today), `Migrate()` on startup is good enough.

---

## P2 — Tests

### VidroProcessor — resilience is not tested (the most expensive gap of the three repos)

The 7 pipeline steps all have `_test.go`, which makes it look well covered. But:

- [x] ~~**`internal/processor/processor.go` (253 lines) — zero tests.**~~ **RESOLVED**
      *(2026-09-07)*. 10 tests in `internal/processor/orchestration_test.go`, package coverage
      **11.1% → 78.0%**, running in 0.1s with a fake step — no FFmpeg.
      Covered: the list of non-critical steps (the four, in order, with `nonCriticalStepCount`
      matching), each step's timeout going through `Options.step`, a non-critical step failure
      that does **not** stop the following ones (sequential) nor cancel its siblings (parallel —
      the reason it is a `WaitGroup` and not an `errgroup`), P-PERF4's FFmpeg cap per job,
      `runStep` (error, timeout, parent cancellation, per-step metric) and a critical step
      aborting the job (`ProcessVideo` with an invalid file; skips without `ffprobe`).
      **It cost a refactor worth having on its own:** the two orchestrators had **separate
      copies** of the list of steps 4–7 — the risk `conventions.md` warned about in prose
      ("register the step in **both** orchestrators") and that `docs/patterns-to-import.md`
      (item 2) wanted to solve with an AST checker. Now there is a single list
      (`nonCriticalSteps`), read by both: the AST checker is no longer needed.
      New test-only dep: `prometheus/client_golang/prometheus/testutil` (a subpackage of a dep
      that was already there; brought `kylelemons/godebug` as an indirect one).
      **Verified by mutation (5×):** `continue`→`return` in the sequential one, an uncapped
      semaphore in the parallel one, `opts.step()` removed from a timeout, a step renamed and the
      metric observation removed — each breaks a different test.
- [x] ~~**`queue/` (283 lines, `client.go` + `job.go`) — zero unit tests.**~~
      **RESOLVED** — `VidroProcessor` `8d1b98d`. 12 tests in `queue/queue_test.go`, coverage
      0% → **60.3%**, running in 8ms.
- [x] ~~**The worker loop lives in `package main`, so nothing can test it**~~ **RESOLVED**
      *(2026-10-05, `1e9b466`)* — the loop became `internal/worker` (`worker.New` + `Run`;
      `main.go` was left with only the wiring). Seam: a `storage` interface with the 5 MinIO calls
      and the pipeline as a `processVideo` field; Redis stays real via `miniredis`. Three tests in
      `internal/worker/worker_test.go` lock #13 (a failed job leaves `:processing` and re-enters or
      goes to the DLQ; a completed job is acked) and the crash path (see the item below).
      **Verified by mutation:** putting `defer cancelBookkeeping()` back in the parent breaks all
      three. *(found on 2026-09-13)*. `test/integration` does not import `main`, and driving
      `processNextMessage` needs Redis **and** MinIO. That is why the `cancelBookkeeping` bug (the
      Processor's design-decisions #13) got through: the parent cancelled the context the goroutine
      was still using to close the job — a failed job stayed stuck in `:processing` with
      `retry_count: 0` and a **completed** job waited for the orphan recovery to reprocess it. Fixed
      and verified on the stack, but the regression only had a **manual check**
      (`VidroProcessor/docs/TESTING.md`). Moving the loop to an importable package is what unlocks
      a real test — and it unlocks the item right below as well.
- [x] ~~The queue integration tests only cover the happy path~~ **RESOLVED** *(2026-10-05)* by
      `TestWorker_CrashMidJobIsRecoveredAndReprocessed`: the worker hangs midway → the job survives
      in `:processing` → `queue.RecoverStuckJobs` (exported for the test) requeues it → another
      worker completes it. `test/integration/queue_test.go` still tests the library, with no new
      value.
      Original text: the queue integration tests (`test/integration/queue_test.go`) only cover the
      happy path: `PublishAndConsume`, `MultipleMessages`, `EmptyQueue`, `SuccessQueue`. None of
      failure — and **none of them imports the `queue` package**: they talk to `go-redis`
      directly, i.e. they test the library, not our code.
      With the `queue/` unit tests in place, the value of rewriting them dropped a lot; what would
      remain useful is an end-to-end failure test (worker dies midway → job survives in
      `:processing` → recovery picks it up).

> **RESOLVED (queue/)** — harness: `miniredis` (new dep, test only, pure Go). The reason for not
> using the testcontainers already installed is the item below itself: a test behind Docker does
> not run. The tests live *inside* the package, so they point the `client`/`cfg` globals at the
> fake and call `recoverStuckJobs` directly, without waiting for the 1min ticker and without
> refactoring production to inject an interface.
>
> **Two findings along the way, both fixed in the same commit:**
> 1. The retry-vs-DLQ decision was not in `queue/` — it was inline in `main.go:225`
>    (`state.RetryCount <= queue.MaxJobRetries`). It became `JobState.ShouldRetry()`. A test that
>    *copies* the condition does not catch drift; the invariant had to be brought into the package.
> 2. **Bug: `recoverStuckJobs` requeued orphans forever.** It incremented `RetryCount` and
>    requeued without ever looking at `MaxJobRetries` — a video that hangs the worker got orphaned
>    every time and never reached the DLQ, unlike the worker's failure path, which respects the
>    limit. Now an exhausted one goes to the DLQ (`client.go:109`).
>
> The two tests that lock this were **verified by mutation**: changing `<=` to `<` in
> `ShouldRetry` breaks `TestShouldRetry_Boundary`; removing the recovery guard breaks
> `TestRecoverStuckJobs_ExhaustedOrphanGoesToDLQ`.
>
> Out of coverage on purpose (the remaining 39.7%): `InitRedisClient` (`log.Fatal`), the ticker
> loop in `StartRecovery` (the logic is in `recoverStuckJobs`, which is tested), `HealthCheck`,
> `GetQueueSize`, `SetJobProcessing`/`SetJobDone` (trivial setters).
- [ ] Aggravating factor: the tests skip themselves without Docker/ffmpeg — in practice they
      rarely run outside CI (which has existed since 2026-08-29 and has Docker + ffmpeg). The 12 in
      `queue/` are the exception: `miniredis` is in-process, they always run.
- [x] ~~**`VidroProcessor/docs/TESTING.md` — outdated "What's Missing" list.**~~
      **RESOLVED** — `VidroProcessor` `8d1b98d`. Coverage table with `queue` at 60.3%, `queue` out
      of "No tests", a new section with the 12 tests, and "What's Missing" rewritten:
      `queue.ConsumeMessage`/`PublishSuccessMessage` left, `processor.go` and what was left
      uncovered in `queue/` came in. Still valid: `config.LoadConfig()`,
      `minio.DownloadVideo()`/`UploadVideo()`, `main.processNextMessage()`, transcode benchmarks.

### VidroFront — façade coverage

- [~] 34 tests in 6 API files with mocked fetch **+ 4 render tests** *(2026-09-13)*:
      `src/tests/comment-list.test.tsx` is the first use of `@testing-library/react`/`jsdom`,
      which were installed and idle. The other 34 components and the 11 routes still have no
      render test — what is missing now is coverage, not harness.
      **The harness cost a new `vitest.config.ts`:** with the TanStack Start plugin in the config,
      React resolves through the `react-server` condition and reaches the test with a null hooks
      dispatcher — every `render` dies in `useState`. The test config loads only `tsconfigPaths` +
      `viteReact`. Pattern recorded in `VidroFront/docs/agents/conventions.md` (section "Teste de
      componente").
- [x] ~~`channels` and `playlists` features without tests~~ **RESOLVED** *(2026-10-05,
      `b6c81c9`)* — 32 tests in 4 files (API + components), 12 mutations verified. It found a real
      bug, fixed in `4df47a8`: `uploadChannelAvatar` did not check the presigned PUT's response
      and a 403 from storage became a success. **The same unchecked PUT remains in
      `features/users/api.ts:22` and `features/videos/api.ts:158`.**
      History: `comments` got the 4 render tests of `CommentList.tsx`, which is the most complex
      component in the app.
      Covered: Edit/Delete only on the user's own comment (the `isOwner`, which was once broken by
      a wrong `currentUserId`), a deleted comment as a tombstone with no actions, logged out with
      no form nor Reply, and load error.
      **Verified by mutation:** changing `isOwner` to `!!currentUserId` and showing the actions row
      on a deleted comment break one test each.
      **A real bug found by the fourth test and fixed along:** with the query in error, the
      component showed "Failed to load comments." **and** "No comments yet." at the same time — the
      same lie to the user `dashboard.tsx` had on 2026-09-07, and the same fix (a named
      `hasNoComments`, excluding `isError`).

### VidroApi — good, just clean up

- [ ] Excellent overall: 294 `[Fact]`/`[Theory]` *(recounted on 2026-09-07; 327 tests running,
      82 unit + 245 integration)*, 43 integration files for 43 features (≈1:1 per endpoint, with
      testcontainers). Nothing to do beyond:
- [x] ~~Delete `tests/VidroApi.UnitTests/UnitTest1.cs`~~ — empty `dotnet new` template, a `[Fact]`
      with an empty body that always passes. **RESOLVED** *(2026-09-07)*.
      327 tests still pass (82 unit + 245 integration).

### Across the three

- [ ] No E2E test of the flow that defines the product: upload → processing → play.
      Each repo tests its own edge; the integration between them is not tested.
- [x] ~~**No HTTP load test of the API.**~~ **RESOLVED** *(2026-09-13)* — `loadtest/`, k6 running
      in a container (nothing installed on the host, no new dep in the repo). Three scenarios:
      `browse` (the `GetVideo` polling + trending + feed), `createVideo` (the API's slice of an
      upload — no video bytes, which is P-PERF6's subject) and `signInStorm`.
      **First measurement, against the compose, nearly empty database:** `browse` p95 **2.11 ms**,
      `createVideo` p95 **7.77 ms**, zero failures; `signup` is the slow route (p95 100 ms — it is
      BCrypt). The rate limiter finally saw pressure: ~10 `acquired` per minute
      (= `AuthPermitLimit`) and the rest 429, **never 5xx**, read from
      `aspnetcore_rate_limiting_requests_total`.
      **The first version of the script measured the wrong thing** — `signInStorm` in
      `constant-vus` with no pause generated 753 thousand rejections at 10k rps and drowned the
      other scenarios; it became `constant-arrival-rate`. Recorded in `loadtest/README.md` along
      with the caveat that matters most: **with an empty database, `trending`/`feed` at 2 ms prove
      nothing about real volume.**
### Contract between services — unlocked by the monorepo (2026-08-30)

Both P0s on this list were contract divergences between services. While they were three repos,
there was nowhere to put the net that would catch them. Now there is. See `docs/MONOREPO.md`,
section "What the migration unlocks".

- [~] **Generate the front's types from the API's OpenAPI.** *(partial — see the block at the end
      of the item)*
      `VidroFront/src/shared/types.ts:64-69` mirrors six backend enums **by hand** (`VideoStatus`,
      `VideoVisibility`, `ReactionType`, `PlaylistVisibility`, `PlaylistScope`,
      `CommentSortOrder`), and each feature's request/response shapes are retyped in
      `features/*/types.ts`.
      **I checked the six: they match the backend today** — five against
      `VidroApi/src/VidroApi.Domain/Enums/` and `CommentSortOrder` against
      `Features/Comments/ListComments.cs:19` *(the enum moved to
      `Domain/Enums/CommentSortOrder.cs` on 2026-09-09)*. The problem is not that they are wrong,
      it is that **nothing guarantees they stay right**: `ReactionType` starts at `1`, not `0`, and
      it is exactly the kind of detail a backend refactor takes along without anyone noticing in
      the front. P0.1 ("4 wrong routes in `features-index.md`") was this same class of drift, only
      in a doc.
      Path: `MapOpenApi()` already exists (`VidroApi/src/VidroApi.Api/Program.cs:76`) but is under
      `if (app.Environment.IsDevelopment())` — to generate in CI, either start the API in
      Development and fetch `/openapi/v1.json`, or add `Microsoft.Extensions.ApiDescription.Server`,
      which emits the JSON at build time without starting anything. Then `openapi-typescript`
      generates the `.d.ts`, and a CI job regenerates and fails if the diff is not empty.
      **It is not drop-in:** the API wraps everything in `{ data: T }` and returns enums as
      `EnumValue { id, value }` — the generated type describes the envelope, and the `apiClient`
      is what unwraps it. Plan the thin layer before swapping the hand-written types.

      > **PARTIALLY RESOLVED** *(2026-09-09)* — and the finding is that **the item, as written, was
      > not achievable**: I set up the generation and the document does not describe what matters.
      >
      > `Microsoft.Extensions.ApiDescription.Server` generates the document at build time, and it
      > comes out like this:
      >
      > | | result |
      > |---|---|
      > | routes / methods / parameters | **43 of 43**, correct |
      > | operations with a **response** schema | **0 of 43** — the handlers return `IResult`, without `TypedResults`/`.Produces<T>()` |
      > | request schemas | 16 operations → **a single schema**, named `Request` (each slice's nested record collides) |
      > | the six enums | `VideoVisibility` and `CommentSortOrder` become `{"type":"integer"}` **without values**; the other four do not even appear |
      >
      > Generating a `.d.ts` from that today **would not catch the drift that motivates the item**.
      > So the item became two gates, each closing a real drift:
      >
      > 1. **`contracts/enums.json`** — golden of the six enums, same pattern as the webhook, read by
      >    both sides: `VidroApi/tests/.../Contracts/EnumContractTests.cs` (reflection; no
      >    container, does not use `IClassFixture<ApiFactory>`) and
      >    `VidroFront/src/tests/enum-contract.test.ts`. Both also fail if the golden lists an enum
      >    that side no longer mirrors.
      >    **Verified by mutation:** changing `"Like": 1` to `0` in the golden turns both red.
      > 2. **Versioned `VidroApi/openapi/v1.json`** + a step in `api.yml` that regenerates and fails
      >    on diff. It locks the 43 routes — P0.1's class ("4 wrong routes in `features-index`").
      >    **Verified by mutation:** renaming `/v1/feed` to `/v1/feeeed` produces a diff.
      >
      > Costs and gotchas, all documented in `VidroApi/openapi/README.md` and in the API's
      > **design-decisions #11**:
      > generation sits behind `-p:GenerateOpenApiDocument=true` (it boots the host; the everyday
      > build does not pay for it); it needs `ASPNETCORE_ENVIRONMENT=Development` because
      > `AddInfrastructure` builds the MinIO client on the spot and the endpoint only exists in
      > `appsettings.Development.json`; and `Program.cs` **skips the migration** when the process is
      > `GetDocument.Insider`, because generating the contract cannot require Postgres to be up.
      > Bonus: the three workflows now trigger on `contracts/**` — without that the golden (the new
      > one **and** the webhook ones, which were already in that situation) could change with no CI
      > seeing it.
      >
      > **What is still open** for the item to really close: the API declaring its responses
      > (`TypedResults`/`.Produces<T>()` in the 43 features), a schema ID per feature to resolve the
      > `Request` collision, and a transformer so the enums come out with values. Only then do
      > `openapi-typescript` + the thin `{ data: T }` envelope layer make sense.

- [x] ~~**Shared contract fixture between Processor and API for the webhook.**~~
      **RESOLVED** *(2026-09-07)*. Four goldens in `contracts/` (`video-processed-*.json`), read by
      both sides: `VidroProcessor/webhook_contract_test.go` proves `buildWebhookPayload` serializes
      **exactly** that, and `VideoProcessedTests.cs` makes a signed POST of the **same file** to the
      real endpoint. Cases: full success, success with no optional artifact and no metadata block,
      success without `processedPath` (→ `Failed`) and permanent failure. The `videoId` is a
      placeholder each side swaps for its test's id.
      The hand-written payloads in the API test were deleted — the 6 tests that used
      `BuildSuccessPayload` now exercise the real contract.
      Along the way, `notifyWebhook` had its `JobState` → `Payload` mapping extracted into
      `buildWebhookPayload` (the rest was sending, not testable without a server).
      **Verified by mutation on both sides:** renaming the `json:` tag of `previewPath` breaks the
      Go test; renaming `processedPath` in the golden breaks the C# test.
      ~~**The rest of the contract is missing, only documented today:** queue name
      (`JobQueueSettings:QueueName` ↔ `PROCESSING_REQUEST_QUEUE`), `callback_url` and
      `correlation_id` in the `JobState`~~, and the MinIO path layout.
      *(2026-09-13: `correlation_id` entered the envelope with tests on both sides and verification
      on the stack — but each side tests its own field name, which is exactly the class of drift
      only a golden catches. Natural candidate for `contracts/job-state.json`.)*
      **DONE** *(2026-10-12)* — `contracts/job-state.json` pins the queue name, the `job:<id>` key
      and the envelope (`status`, `callback_url`, `correlation_id`, `retry_count`, timestamps).
      API: `RedisJobQueueService.SerializeJobState`/`JobStateKey` extracted so `PublishJobAsync` and
      `JobStateContractTests.cs` (unit, no Docker) call the same code, compared with
      `JsonNode.DeepEquals`; `JobQueueSettings:QueueName` from `appsettings.json` against the
      golden. Worker: `job_state_contract_test.go` seeds the golden under `stateKey` in miniredis
      and reads it through `queue.GetJobState`, decodes with `DisallowUnknownFields` (an API-side
      rename would otherwise come back as an empty field and pass) and re-encodes to catch a
      renamed tag; `PROCESSING_REQUEST_QUEUE` is read from the root `docker-compose.yml` and
      `.env-example`, so `processor.yml` now also triggers on `docker-compose.yml`.
      **Verified by mutation, 10 of 10 red:** in the golden, renaming `correlation_id` (both
      sides), the queue (both) or the `job:` prefix (both); in C#, renaming `callback_url`, the
      key prefix, or the `appsettings.json` queue; in Go, renaming the `correlation_id` tag or the
      `jobKey` prefix, or the queue in compose or in `.env-example`.
      **Only the MinIO path layout is still discipline.**

### Flow reliability — findings from the end-to-end doc *(2026-10-05)*

Found while writing `docs/end-to-end-flow.md`. The serious risks were closed in wave B
(2026-10-05).

**Doc/comment that lies:**
- [x] **RESOLVED** *(`1eaa603`, queue removed — also closed the "failure after archiving")*.
      ~~**Nobody consumes `video_success_queue`.** The worker publishes (`VidroProcessor/queue/client.go`,
      `PublishSuccessMessage`), no API code reads it. Claiming otherwise:
      `docs/troubleshooting-stuck-video.md:164,192`, `VidroProcessor/docs/agents/architecture.md:39`,
      `design-decisions.md:113`. Decide: fix the docs or stop publishing.~~
- [x] **RESOLVED (doc)** *(`6581924`)*: only `ConsumeMessage`/`AcknowledgeMessage` go through the
      breaker, and the doc now says so; wrapping the rest is an open decision (bookkeeping cannot
      stop with the breaker open). `RecoverStuckJobs`'s `LRem`/`LPush` now check the error
      (`96ce073`, `ed9adac`, `2d97407`). Before: the comment in
      `internal/circuitbreaker/circuitbreaker.go:19` said the Redis breaker covers "job state";
      `setJobState` (`queue/job.go`) and the recovery call Redis directly. Same for
      `RecoverStuckJobs`'s `LRem`/`LPush`, which still ignored the error — and `conventions.md`
      said every Redis call goes through the breaker.
- [x] **RESOLVED** *(2026-10-06)* ~~`docs/observability.md` says the worker logs carry
      `correlationID`; the `notifyWebhook` and `RecoverStuckJobs` ones use the global logger and
      go out without it.~~ `JobState.Logger(videoID)` builds the logger with `videoID` +
      `correlationID` and both paths now use it (plus the permanent rejection log of
      `webhook.Notify`). An orphan without `job:` has nowhere to read the id from.

**Behavior risks:**
- [x] **RESOLVED** *(`c6ba3da`: 90 min, worker worst case = 80 min locked in
      `contracts/processing-timeout.json` by both sides; a webhook outside `Processing` now logs a
      warning. A queue backlog above ~10 min still blows it — marked with `ponytail:`)*.
      ~~**The API's 45 min timeout sized for one attempt** (`VideoSettings.cs:9-14`). A job that
      uses its retries goes past it → the video becomes `Failed` → the later success webhook is
      silently dropped (200).~~
- [x] **RESOLVED** *(`16172fc`: `SetJobProcessing` returns `ErrJobStateMissing` and the worker sends
      it to the DLQ unprocessed, raw intact)*. ~~**`job:<id>` expired or deleted** →
      `SetJobProcessing` (`queue/job.go`) recreates the state without `callback_url` → the video is
      processed and nobody is told.~~
- [x] **RESOLVED** *(`94147fd`: save → publish → commit in a transaction; a failed publish rolls
      back and the video stays `PendingUpload` for the reconciliation to republish. API
      design-decisions #14)*. ~~**`MinioUploadCompleted.cs:89-91` publishes to the queue before
      `SaveChanges`.** If the save fails, the job runs and the webhook is ignored; reconciliation
      later marks `Failed`.~~
- [x] **RESOLVED** along with the removal of `video_success_queue` (`1eaa603`): the publish was the
      only critical step after archiving. One narrow case remains: Redis down when the job closes.
      ~~**A failure after archiving the raw has no possible retry:** if the success publish fails,
      `raw/<id>` has already gone to `raw-archived/`, every retry fails on download and the video
      becomes `Failed` with the artifacts in the bucket.~~
- [x] **RESOLVED** *(2026-10-06)* ~~**The webhook may point at a file that does not exist:**
      `buildJobArtifacts` builds the path from the pipeline result, not from the upload; and the
      total of 5 thumbnails is fixed apart from `thumbnail.go:22`.~~ A failed optional upload zeroes
      the field in the result before `buildJobArtifacts`; the 5 became
      `processor_steps.ThumbnailCount`, read by the step and by the webhook.
      Locked by `TestProcessNextMessage_FailedOptionalUploadIsNotReported`.
- [x] **RESOLVED** *(2026-10-06)* ~~An exhausted orphan goes to the DLQ **without a webhook**
      (`queue/client.go`, `RecoverStuckJobs`)~~ — `StartRecovery`/`RecoverStuckJobs` receive an
      `onDeadLettered` and `main.go` wires it to `Worker.NotifyDeadLettered` (the same path as normal
      exhaustion). An orphan without `job:` has no `callback_url` and goes on without a webhook.
- [x] **RESOLVED** *(2026-10-06)* ~~A permanent error (invalid video, over the limit) is tried 4
      times as if it were transient.~~ A video ffprobe rejects becomes `processor.ErrInvalidVideo`
      and the worker goes straight to the DLQ with a failure webhook. A step timeout and a missing
      ffprobe remain transient. "Over the limit" has no check in the worker, so it got no
      classification.
- [x] **RESOLVED** *(`85bd281`, `notEmpty`)*. ~~`WEBHOOK_SECRET` is optional in the worker
      (`config/config.go:34`) and required in the API: empty, every delivery gets a 401. It should
      fail at startup.~~

**Mess in `internal/worker`** (code moved without change in `1e9b466`):
- [x] **RESOLVED** *(2026-10-06)* ~~`Run` spins without backoff when `processNextMessage` fails
      immediately (Redis down, breaker open).~~ A queue read failure (`errConsume`) pauses 1 s, cut
      short by shutdown; a failing job does not pause. Locked by
      `TestRun_PausesAfterQueueReadError`.
- [x] **RESOLVED** *(2026-10-06)* ~~`worker.go:117` — `if msg == nil` is unreachable;
      `worker.go:129,299` have an inline call inside the `if`~~ — removed and extracted into named
      variables.
- [x] **RESOLVED** *(2026-10-06)* ~~`architecture.md:64` still talks about a "5-min hard
      timeout"~~.

**Findings from waves B and C** *(2026-10-05)* — all fixed; what remains is at the end of the list:
- [x] **RESOLVED** *(`6ab1353`)* `VideoReconciliationService` published before the batch save — the
      same bug as `94147fd`; now it saves → publishes → commits per video, inside a transaction.
- [x] **RESOLVED** *(`cf24aa3`; requeue `96ce073`)* `queue/job.go` — `SetJobDone`/`SetJobFailed`
      still recreated missing state without `callback_url`; `RecoverStuckJobs` skipped a job in
      `:processing` without state, which stayed there forever; `GetJobState` reported any Redis
      error as "job not found".
- [x] **RESOLVED** *(`49c4d4d`)* `internal/webhook/webhook.go:76` — the `if secret != ""` branch
      became unreachable with `WEBHOOK_SECRET` required.
- [x] **RESOLVED** *(`35a1aba`)* `docs/troubleshooting-stuck-video.md:56,87` and
      `VidroProcessor/docs/GETTING_STARTED.md` talked about 3 attempts / `retry_count 3`; the real
      thing is 4 attempts and `retry_count` 4.
- [x] **RESOLVED** *(`8ecdefb`)* Front, video forms: `.max()` without a message in
      `EditVideoForm.tsx:32-33` and `UploadVideoForm.tsx:50-51`; description without `trim` in
      `EditVideoForm.tsx:81` and `?? null` in `UploadVideoForm.tsx:224` (same bug as `2ccf91b`).
- [x] **RESOLVED** *(`8ecdefb`, doc fixed)* `VidroFront/docs/agents/conventions.md` said the 8 forms
      use shadcn's `FormField`; only `SignInForm`/`SignUpForm` do.
- [x] **RESOLVED** *(`dd1bcf3`)* `uploadVideoFile` did not remove the abort listener from the
      `signal`.
- [x] **RESOLVED** *(`7066c3b`, `c930a66`, `5a78508`, `beb8b1c`, `65a38d6`, `904c9ff`, `c346e34`,
      `6725b88`, `cd3dca4`, `e262309`, `d8fc3a1`)* Wave C findings: an already aborted `signal`,
      controlled channel `<Select>`, per-video `Processing` rescue, dead result of the webhook
      handler, "Step N/M" logs, malformed JSON → 400, cancellation not logged as an error, webhook
      without retry on a permanent 4xx, `GenerateTestVideo` honoring the duration, Biome infos.
- [x] **RESOLVED** *(2026-10-06)* ~~`moveFromProcessing` does not undo the `LREM` if the `LPUSH`
      fails with an execution error (WRONGTYPE)~~ — the MULTI/EXEC became a Lua script that does
      `LPUSH` and only then `LREM`; an error in the push aborts before the remove and the job stays
      in `:processing` for the next sweep. Locked by
      `TestMoveFromProcessing_FailedPushKeepsJobInProcessing` (verified by mutation: `LREM` first
      breaks it).
- [ ] **Still open (decision):** `AcknowledgeMessage`/`RequeueJob` and the state writes are still
      without a breaker.

**Front:**
- [x] **RESOLVED** *(`379955e`)*. ~~The same presigned PUT without checking `ok` in
      `features/users/api.ts:22` and `features/videos/api.ts:158` (the channel avatar one was
      fixed in `4df47a8`).~~
- [x] **RESOLVED** *(`2ccf91b`)*. ~~`CreateChannelForm.tsx:53` sends `''` instead of `null` for an
      empty description (the edit ones send `null`); `:34` without its own message on `.max()`.~~
- [x] **RESOLVED** *(`6ec743a`)*. ~~Visibility without an accessible name: `<Label>` without
      `htmlFor` in `CreatePlaylistForm.tsx:108` and `EditPlaylistForm.tsx:99`.~~

---

## P3 — Frontend UI / UX

**What is already good (do not touch):** consistent states — skeletons on 7 screens, empty states
in 12 places with their own text, `isPending` handled in 23 files, toasts (`sonner richColors`),
forms with react-hook-form + zod, coherent shadcn/ui.

### Features built and switched off

- [x] ~~**Search is a dead feature end to end.**~~ **RESOLVED** *(2026-09-07)*.
      `searchVideos` + `useSearchVideos` (cursor, `SEARCH_LIMIT` 20), `search.tsx` reads the query
      from `?q=` via `validateSearch` and renders the `VideoGrid` with "Load more", and the `Header`
      got a `<search>` with the field. **Mind the contract:** `limit` is required on the endpoint
      (`SearchVideos.cs`, `int limit` with no default) — omitting it gives a 400.
- [x] ~~**`src/components/ThemeToggle.tsx` is dead code**~~ **RESOLVED** *(2026-09-07)*.
      Plugged into the `Header`. The component came from another project and styled with
      `--chip-bg`/`--chip-line`/`--sea-ink`, which **do not exist** in `styles.css` — it would have
      rendered unstyled. Redone with shadcn's `Button` + icon (`Sun`/`Moon`/`Monitor`).
      Left as debt: the light flash on first paint (the theme is only applied after hydration);
      fixing it needs an inline script in the `shellComponent`'s `<head>`, marked with `ponytail:`
      in the file.

### Finishing

- [x] ~~**The Header is not responsive**~~ **RESOLVED** *(2026-09-07)*. CSS only, no drawer nor
      new component: (a) the text label becomes `sr-only` up to **lg** — `sr-only`, not `hidden`,
      so the accessible name stays for the screen reader — and `Dashboard`, which was text only,
      got an icon (`LayoutDashboard`); (b) below **sm** the search leaves the first row and takes
      the whole second one (`flex-wrap` + `basis-full` + `order-last`), with the header at
      `min-h-14 py-2` instead of `h-14`.
      **Measured in the browser** (agent-browser, authenticated header, the fullest case) — the
      lesson of `docs/patterns-to-import.md` item 17 is exactly this, a broken layout does not fail
      a content test:
      | width | overflow | header height | search width |
      |---|---|---|---|
      | 320 | no | 89px | 288px |
      | 375 | no | 89px | 343px |
      | 640 | no | 57px | 313px |
      | 1024 | no | 57px | 402px (labels show) |
      | 1280 | no | 57px | 448px |
      **The first attempt passed and was wrong:** just hiding labels from `sm` there was no
      overflow anywhere, but the search was left with **17px** at 320px and **18px** at 640px — it
      fit because the field was crushed. That is why the assertion is the search width, not just
      `scrollWidth <= clientWidth`.
- [x] ~~**`<html lang="pt-BR">` with the whole UI in English.**~~ **RESOLVED** *(2026-09-13)*.
      **Repo owner's decision: English.** `lang="en"` in `__root.tsx` and "Meu Perfil" →
      "My profile" in the Header — they were the only two occurrences of Portuguese in the UI (I
      swept `src/**/*.tsx`). Dates were already `en-US`, so nothing else changed.
- [x] ~~**SEO is zero.**~~ **RESOLVED** *(2026-09-13)*. A `seo()` helper
      (`src/shared/lib/seo.ts`) builds title + `description` + Open Graph + Twitter card, and the
      **9 routes** declare their `head`: home, search (with the term), user and channel (from the
      params), playlist and watch (from `loaderData`), and the three private ones with `noIndex`.
      **Watch is the point of the item** — it is the link that gets pasted in chat: video title,
      description and the thumbnail's `og:image`, `og:type: video.other`.
      **The trap that decided the design:** for `head` to see the data, the loader has to return
      something — but swapping `prefetchQuery` for `ensureQueryData` would make a failed video take
      down the **page** instead of just the meta tag. The loader returns whatever is in the cache
      (`queryClient.getQueryData`), and without data `head` falls back to the root default.
      **Verified running** (`bun run dev` + `curl`, real SSR): home, `/search?q=jazz` (with
      `robots: noindex`), `/alice/music` and `/dashboard` come out with the right head, and
      `/watch/<nonexistent id>` with the API down falls back to the default without breaking the
      page.
      **Not verified:** the watch head with a real video — it needs the stack up with seeded data.
      Tests: `src/tests/seo.test.ts` (title suffix, large card only with an image, `noIndex`).
      **Still open:** `og:url` is not emitted (there is no configured public URL; the crawler falls
      back to the URL it fetched) and the watch `og:image` is a MinIO presigned URL — **it
      expires**, so a card cached for too long loses its image. Fixing it needs a public artifact
      or a proxy.
- [x] ~~**No `errorComponent` or `notFoundComponent` on any route.**~~ **RESOLVED**
      *(2026-09-07)*. Instead of 11 routes, **one place**: `src/router.tsx` registers
      `defaultErrorComponent` and `defaultNotFoundComponent` (`components/RouteFallback.tsx`),
      which every route inherits — a route only declares its own when it can do better. The error
      fallback has "Try again" (`router.invalidate()`) + a link home, and shows `error.message`
      only in `import.meta.env.DEV`.
      **Verified running:** `/a/b/c/d` returns the "Page not found" screen already in SSR. The
      *error* fallback cannot be checked with `curl`: a throw in render during SSR makes React
      switch to client rendering (`Switched to client rendering because the server rendering
      errored`), so the browser is what renders the screen.
      **Found along the way:** `/route-that-does-not-exist` is **not** a 404 — it matches
      `/$username`, which renders "route-that-does-not-exist's channels". A one-segment route will
      always land on `$username`; a nonexistent user's 404 is that route's `isError`, not the
      router's.
- [x] ~~`index.tsx` (home) and `dashboard.tsx` handle `isPending` but not `isError`.~~
      **RESOLVED** *(2026-09-07)*. Home: `FeedSection` and `TrendingSection` with their own
      message. Dashboard: **it was lying to the user's face** — with the query failing,
      `isPending` turns false and `playlists.length === 0`, so it showed "No personal playlists
      yet." instead of an error. Now `playlistsFailed` has its own branch and the empty state
      requires `!playlistsFailed` (via `hasNoPlaylists`/`hasPlaylists`, named instead of composed
      inline).
- [x] ~~**Devtools ship to production**~~ **RESOLVED** *(2026-09-07)* — `<TanStackDevtools>` now
      behind `import.meta.env.DEV` in `__root.tsx`.
- [~] **Shallow a11y** — only ~10 files have any `aria-*`/`alt`/`sr-only`.
      *(2026-09-07: the Header stopped being the worst case — `SearchBox` and `ThemeToggle` have
      `aria-label`, and search uses the native `<search>` element. The rest of the app is still
      shallow, and the `<video>` without `<track>` became a P5 item.)*
      *(2026-09-13: closed the controls whose **icon was the only meaning**.* The 6 reaction
      buttons (`CommentList`, `ReplyList`, `watch.$videoId`) had an empty accessible name when the
      count was zero — "button, button, button" on the screen reader — and in `watch` the name was
      just the number. They got `sr-only`, the pattern the Header already used. The 6 `VideoCard`
      stats (views/likes/dislikes) were a loose number: now they read "12 views".
      Locked by test: `comment-list.test.tsx` queries the buttons by accessible name (`Like`,
      `Dislike 3`) — **verified by mutation**, removing an `sr-only` turns it red.
      *The real icon-only buttons (`Trash2` in `dashboard`/`PlaylistItemList`, `Pencil`/
      `ImagePlus` in `VideoCard`) already had `aria-label`.*
      **Still open:** nobody has run an audit (axe/Lighthouse) on the app; what was done here came
      from reading code, not from a tool.)

---

## P4 — Documentation for humans

- [x] **Root README** *(2026-08-30)* — what Vidro is, the three services, how to start the stack,
      ports/credentials, tests, conventions and an index of the root docs.
- [x] ~~**There is no end-to-end flow doc.**~~ **RESOLVED** *(2026-10-05, `9fec5e8`)* —
      [`docs/end-to-end-flow.md`](docs/end-to-end-flow.md): states, times and limits, the 8
      handoffs with payload/idempotency/failure, and "who fixes what". Writing it found 11
      divergences and risks — see "Flow reliability" in P2.
      Original text: there was no end-to-end flow doc. Each service documents its own box; the
      path that matters — upload → presigned PUT → `minio-upload-completed` webhook → Redis queue
      → pipeline → `video-processed` webhook → HLS in the player — crosses all three. It is the
      hardest thing to reconstruct alone.
      *(2026-08-30: the root `README.md` now carries this flow in 8 steps + a diagram. It serves as
      a map, it does not replace the doc — it lacks each payload's shape, retries, and what happens
      when each handoff fails.)*
- [x] ~~**`VidroApi/README.md` has 46 lines and the quickstart does not work.**~~ **The item was
      what was outdated** *(checked on 2026-09-13)*. The file has 60 lines and already covers the
      whole stack, the API alone, ports, the migration-on-startup note and the EF commands — it was
      fixed in some earlier batch without striking the item here (the same class as P0.1, now in
      the TODO).
      **Verified running**, not reading: `dotnet run --project src/VidroApi.Api` against the
      compose's `postgres`/`redis`/`minio` starts, `/health` answers `Healthy` and
      `GET /v1/videos/trending?limit=3` returns 200.
- [x] ~~**60% of the project's markdown is archaeology.**~~ **RESOLVED** *(2026-09-13)*.
      `VidroApi/docs/plans/` and `VidroFront/docs/plans/` deleted: **5,194 lines**, 8 files, zero
      open checkboxes across the eight. The history stays in git; what goes away is the cost of
      sweeping completed-phase docs on every task.
      Pointers adjusted in the same batch (`VidroApi/CLAUDE.md`, `VidroFront/CLAUDE.md`,
      `design-decisions.md #10`) — a doc pointing at a nonexistent file is the same class as P0.1.
      **Two things were rescued before deleting**, because they were future and not history:
      multipart upload with resume (became a P5 item below) and notifications, which were already
      in P5. The rest of the "Future improvements" section of the API design had already been done
      (search) or was already lying (`MediatR`, which the repo never used — it is `Mediator` with a
      source generator).

---

## P5 — Product (bigger work, but it is what the user feels)

- [ ] **There is no watch history.** There is `RegisterVideoView.cs` with dedup per window, but no
      history entity/endpoint, and no `/history`, `/liked` or `/subscriptions` route in the front.
      The most obvious gap for a video platform.
- [ ] **No notifications** of a new video on a subscribed channel. `ChannelFollower` already
      exists; the rest is missing.
- [ ] **Upload is a single presigned PUT, without resume.** A large video that loses the connection
      starts over from zero. The path planned since the API design is the S3 Multipart API with
      the parts' state in the client's `localStorage`; it crosses the API
      (`design-decisions.md #10`) and the front. Until the product has really large uploads, it is
      speculative.
- [ ] **No subtitles/captions anywhere.** The pipeline has no subtitle step, the API has no field,
      and the `<video>` in `VideoPlayer.tsx` goes out without a `<track>` — suppressed today with
      `biome-ignore lint/a11y/useMediaCaption` **naming the gap**, not pretending it does not
      exist. For anyone who cannot hear, every video on the platform is inaccessible. It crosses
      all three services.

---

## P6 — VidroProcessor: pipeline performance and scalability

Coming from the old `VidroProcessor/docs/roadmap.md`, absorbed here on 2026-08-30 — it was a
second backlog of the same worker, with no item in common with this file. What was already done
there was not copied: whoever wants to know what exists reads
`VidroProcessor/docs/agents/features-index.md`, which maps feature → file.

**State on 2026-09-03:** P-PERF1 to P-PERF4 are **implemented in the code** and were marked `[ ]`
here — the same class of drift as P0.1 ("docs that lie"), now on the TODO side. Fixed below with
`file:line`. **The gain was never measured:** the "~2–3 min" goal has no before/after, but P-PERF6
with a real video *(2026-10-08)* makes it plausible: ~26 s per job for 55 s of 1080p, ~2 min
extrapolating to 500 MB.

**Original cost:** a ~500 MB / 1080p video took several minutes.

### 🔴 High priority

- [x] ~~**P-PERF5: `WORKER_COUNT` × parallel steps oversubscribes the CPU.**~~ **RESOLVED**
      *(2026-09-07)*. `processor.DefaultWorkerCount(numCPU, parallelSteps, maxParallelSteps)` =
      `numCPU / FFmpeg processes per job`, floor 1; `workerCount(cfg)` in `main.go` (same pattern
      as `jobTimeout`/`JobBudget`). On an 8-core host: **2 workers** instead of 8, and ~8
      simultaneous FFmpeg instead of up to 32. Turning off `PARALLEL_NON_CRITICAL_STEPS` or
      lowering `MAX_PARALLEL_POST_TRANSCODE_STEPS` to 1 gives back 1 worker per core by itself — no
      new knob. `WORKER_COUNT > 0` still wins.
      The `[1,4]` clamp that was inline in `runNonCriticalStepsParallel` became
      `clampParallelSteps`, shared with the default: a test that copies the condition does not
      catch drift (the same lesson as the `queue/` tests).
      Docs rewritten: decision **#9** (title, anchor and index), `config.md:41`,
      `architecture.md:27`, `features-index.md:9`, `GETTING_STARTED.md:213`.
      Tests: `internal/processor/workers_test.go` — the invariant is
      `workers × parallel steps ≤ cores`. **Verified by mutation:** changing `processesPerJob` to
      `1` breaks 2 of the 4 tests.
      **Measured** *(2026-10-06)*: see P-PERF6 — the knee of the curve is at 2 workers, same as the
      default. With a real video *(2026-10-08)* the curve is flat: 2 workers neither gain nor lose.

### 🟡 Medium priority

- [x] **RESOLVED** *(`98600b7`)* `ENABLE_THUMBNAILS/AUDIO/PREVIEW/STREAMING` flags (on by default),
      the contract already accepted omitted artifacts; `JobBudget` keeps the value with everything
      on. ~~**P-OPT1: make the non-critical steps optional.** Steps 4–7 are not needed for every
      product surface: today the front plays the **processed MP4** + thumbnails — `GetVideo`
      builds `videoUrl` from `ProcessedPath` and never exposes `hlsPath`.
      Config (env flags) to **skip** any combination reduces FFmpeg time and MinIO writes. Critical
      path: validate → analyze → transcode → upload.~~
      **Coordination with the API:** the webhook payload / `VideoArtifacts` must accept omitted
      paths where they are already nullable (`HlsPath`), and the `VideoProcessed` handler + cleanup
      must tolerate a missing optional artifact.

      > ⚠️ **Do not read this as "HLS is dead code".**
      > `VidroFront/src/features/videos/components/VideoPlayer.tsx` already detects `.m3u8` and
      > plays it via `hls.js`; the player is wired, only the API field is missing. HLS is one
      > `GetVideo` change away from becoming the main playback path — put the step behind a flag,
      > do not delete it.

### 🟢 Low priority

- [x] **P-PERF6 — benchmark done** *(2026-10-05; real video on 2026-10-08)*.
      `internal/processor/pipeline_bench_test.go`
      (`go test ./internal/processor -run '^$' -bench . -benchtime 3x -count 3`; table and how to
      run it in `VidroProcessor/docs/TESTING.md`). Synthetic 10 s clip, 8 cores/16 threads, ffmpeg
      9.0.2: transcode 0.45 s; steps 4–7 sequential 1.48 s, parallel max=1 1.47 s, max=2 1.00 s,
      max=4 0.97 s; `ProcessVideo` with everything on 1.47 s against 0.50 s with `ENABLE_*` off.
      **Confirms:** P-PERF3 (~1.5× with parallel; almost all the gain already at max=2) and P-OPT1
      (−66%).
      **Does not confirm:** P-PERF1/2 (no before/after, they already are the code), nor the
      "~2–3 min" — 10 synthetic seconds are not 500 real MB.
      **Second half** *(2026-10-06)*: `BenchmarkConcurrentJobs` (batch of 8 jobs, 1/2/4/8 workers):
      11.7 s → 9.8 s → 9.5 s → 9.4 s. **Confirms P-PERF5:** the knee is at 2 workers, which is the
      `DefaultWorkerCount` on this host; more than that does not pay off.
      **Real video** *(2026-10-08)*, `BENCH_VIDEO` with 55 s of 1080p30 HEVC (100 MB), `-count 1`:
      205 s → 202 s → 207 s → 207 s — **flat**, difference within the noise. ~26 s per job: a single
      job already saturates the 16 threads, so the extra worker neither helps nor hurts (the gain
      in the synthetic one was process startup). Extrapolating by duration, 500 MB at the same
      bitrate ≈ 2 min: P-PERF1–4's "~2–3 min" becomes plausible, still without before/after.

### ~~Done~~ ✅ — P-PERF1 to P-PERF4 *(marked on 2026-09-03; the code was already there)*

- [x] ~~**P-PERF1: HLS in a single command (biggest impact).**~~
      `segmentForStreamingSingleCommand` (`internal/processor/processor-steps/streaming.go:114`)
      builds a single invocation, with `-filter_complex` (`:134`) and one `-map` per variant —
      FFmpeg reads the input once. The sequential loop survived on purpose, as a fallback
      (`:100`).
- [x] ~~**P-PERF2: transcode preset `medium` → `fast`.**~~ `transcode.go:32` uses `-preset fast`,
      CRF unchanged. The NVENC path uses the preset from `NVENC_PRESET` (default `p5`).
- [x] ~~**P-PERF3: parallelize the non-critical steps 4–7.**~~ `runNonCriticalStepsParallel`
      (`internal/processor/processor.go:195`): a semaphore sized by
      `MaxParallelPostTranscodeSteps`, `sync.WaitGroup` and a mutex on `result`. It does **not**
      use `errgroup` as the item planned — a failing non-critical step must not cancel its
      siblings, which is exactly the policy `errgroup` would apply.
- [x] ~~**P-PERF4: guard rails for the optimized pipeline.**~~ All four in place: a cap on
      parallel FFmpeg per job (`processor.go:197-203`, clamped to `[1,4]` — passing more than 4 is
      silently ignored, and 4 is the number of steps); per-step metric preserved
      (`processor.go:276`, `metrics.ProcessingStepDuration`); fallback from single-command HLS to
      sequential (`streaming.go:86-97`, behind `HLS_SINGLE_COMMAND_FALLBACK`); and the four env
      flags in `config/config.go:41-44`, documented in `docs/agents/config.md:48-51`.

      > P-PERF4's cap protects against a CPU/RAM spike **within a job**. It does not see the other
      > workers on the same host — that is the gap P-PERF5 closes.

**Long term — scalability:**

- [ ] **Auto-scaling:** add workers according to the queue size.
- [ ] **Horizontal scaling:** several worker instances on different machines.
      It is what would make the hardware profile matrix discarded in P-PERF6 start to make sense:
      with a single machine, it does not.
- [ ] **Queue priority:** short videos first, long ones in a separate queue.

---

## Small mess

- [x] ~~**Clean up what `biome check` reports in `VidroFront`**~~ **RESOLVED** *(2026-09-07)*.
      Zero errors; `bun run lint` (`biome ci`) runs in `.github/workflows/front.yml`.
      **It cost a convention decision.** Of the 109 errors, **72 were "file needs formatting"**,
      and the Biome formatter **collapses a short ternary onto one line**, against the root
      `CLAUDE.md` rule that applied to all three services — with no Biome option to preserve it.
      Resolved by **relaxing the rule**: a short ternary may now fit on one line in all three
      services, and the formatter stayed on with the config aligned to the code (2 spaces, single
      quotes, no semicolons). 62 files reformatted in a mechanical commit, plus 33 for import order
      and `useImportType`.
      The 8 by hand: the 3 `noArrayIndexKey` were a **misplaced** `biome-ignore` (the suppression
      only applies to the next line and the `key={i}` fell two lines below); the 3
      `useExhaustiveDependencies` were "suppressed" by an `// eslint-disable-next-line` — **a dead
      comment, this repo does not use ESLint** — and became an honest dependency (`loadedVideoId`
      in the effect body, because navigating between videos reuses the route instead of
      remounting); 1 `noStaticElementInteractions` is a false positive (decorative preview on
      hover, the card is already a focusable `Link`) and 1 `useMediaCaption` is a **real gap**, see
      P5.
      11 `noNonNullAssertion` and 7 infos remain, all **warnings** — they do not break CI. Each `!`
      is still a potential crash (same class as BUG-1); it stays as its own item.
      **Two Biome traps that cost time and are documented in the front's `CLAUDE.md`:** a `//`
      comment in `biome.json` breaks the config **silently** (it falls back to the default and
      starts linting `dist/`: 83 → 148 files), and a `biome-ignore` with a two-line reason becomes
      an "unused suppression" without applying the rule.

- [x] ~~**`noUncheckedIndexedAccess` off in the front**~~ **RESOLVED** *(2026-09-07)*.
      Enabled in `tsconfig.json`. **It cost 4 fixes, all in tests** — the production code passed
      clean on the first try because it already handled indexing with `?? undefined`/`?.`
      (`video.thumbnailUrls[0] ?? undefined`). The 4 were `mockFetch.mock.calls[0]` destructured
      directly; they became a guard with its own message instead of `!`, which `biome` would
      report. Verified that the flag really bites (a temporary `const first: string = items[0]`
      breaks the build). Rule written in `VidroFront/docs/agents/conventions.md`.

- [x] ~~**The 11 `noNonNullAssertion` left in the front.**~~ **RESOLVED** *(2026-09-13)*.
      Zero `noNonNullAssertion` in `biome check`. The 9 in `queryFn` became `skipToken` — the
      `enabled: !!x` went with them, because `skipToken` already leaves the query in the same
      state, and now it is the **type** that guarantees the parameter instead of a promise to the
      compiler. Rule recorded in `VidroFront/docs/agents/conventions.md`.
      The 2 in `CommentList`/`ReplyList` were something else: `content` really is
      `string | null` in the contract (deleted comment), and the `!` was in a branch that is
      **not** the `isDeleted` one — they became `?? ''`.
      34 tests passing, `typecheck` and `build` clean.

- [x] ~~**Nothing ran `tsc --noEmit` in the front** — neither a script nor CI.~~ **RESOLVED**
      *(2026-09-07)*. `"typecheck": "tsc --noEmit"` in `package.json` + a **Typecheck** step in
      `.github/workflows/front.yml`, before test.
      **The doc underestimated it:** `docs/patterns-to-import.md` (item 15) said "one line each" —
      the compiler reported **23 pre-existing errors** in 11 files, all zeroed along: 8 unused
      symbols, 5 of `ReactionType` (a const, not a type) used as a type — hence the new
      `ReactionTypeValue` in `shared/types.ts` —, 6 of `react-hook-form` generics
      (`z.coerce.number()` produces an `unknown` input in zod 4; it became `z.number()`, since the
      two affected `setValue` calls already converted with `Number()`), 2 of narrowing that does
      not survive inside a closure in `watch.$videoId.tsx`, and 1 **real bug**:
      `currentUserId={currentUser?.id}` — `UserProfile` has `userId`, not `id`, so
      `CommentList`'s `isOwner` was always false and nobody saw the edit/delete buttons on their
      own comment. It was exactly the kind of error the gate exists to catch.

- [x] ~~**No linter in `VidroProcessor`**~~ **RESOLVED** *(2026-09-07)*. `.golangci.yml` with 15
      linters, each one a rule already written in prose in `conventions.md`: `errorlint` (`%w`),
      `nilerr`, `forbidigo` (`os.Getenv` only in `config/config.go`, `time.Now` outside the steps),
      `depguard` (zerolog in the worker; a pipeline step does not import MinIO/Redis), `godox`
      (FIXME/HACK/XXX banned, **TODO allowed**), `noctx`, `bodyclose`, `nolintlint` + the cheap
      ones. Formatters `gofumpt` + `goimports`.
      The **Lint** step in `processor.yml` replaced the `gofmt` and `go vet` steps.
      **It cost 74 findings**, all zeroed. The ones that were real bugs and not style:
      - `queue/client.go` did `result.(*Message)` without checking — a raw assertion on the
        circuit breaker's `any` return, i.e. a panic in the worker if the type changed.
      - 5 `fmt.Errorf(... %v, err)` in `main.go` broke the callers' `errors.Is`, and an
        `err != context.Canceled` in the worker loop would fail with a wrapped error.
      - `streaming.go:258` discarded the output of the first NVENC attempt (`ineffassign`).
      - `main.go` swallowed the `shutdownTracing` error in the `defer`; now it logs it.
      The rest was mechanical: 16 formatting, 13 `//nolint:errcheck` that had become redundant in
      tests, 5 `intrange`, `os.Remove`/`w.Write` with an explicit `_ =`, `exec.Command` →
      `CommandContext(t.Context())` in the test helper and `http.NewRequestWithContext` in the
      webhook (with the reason for `context.Background()` written in the code).
      The only new `//nolint`: `BRPopLPush` (SA1019), which is a documented decision (#1).

- [x] ~~**The job's `context` does not cross `queue/` and `minio/`**~~ — found through the 16
      `contextcheck` warnings, which is why it was left **off** in `.golangci.yml`.
      The public functions of both packages (`GetJobState`, `SetJobFailed`, `HealthCheck`,
      `GetQueueSize`, `DownloadVideo`, `UploadVideo`, `UploadDirectory`, `ArchiveRawVideo`,
      `PublishSuccessMessage`...) do not accept a `context.Context` and use `context.Background()`
      inside. Real consequence: when the job budget runs out or the worker gets `SIGTERM`, the
      cancellation **does not reach** Redis or MinIO — the 500 MB download carries on to the end,
      and the 30s graceful shutdown cap (design-decisions #12) counts on operations that do not
      know they should stop.
      It is a refactor of both packages' public signatures (+ call sites in `main.go`), not lint
      cleanup. Once done, turn `contextcheck` back on.

      > **RESOLVED** *(2026-09-09)*. The 11 public functions in `queue/` and the 6 in `minio/`
      > receive `context.Context` as the first parameter and pass it on to Redis/MinIO;
      > `contextcheck` is **on** in `.golangci.yml`, and of the 16 findings **2** remain, both
      > `go notifyWebhook` — detached on purpose, with a `//nolint:contextcheck` naming the reason
      > (the webhook goes out from a defer, almost always with the job's context already
      > cancelled; what bounds it is the HTTP client's own 10s timeout).
      >
      > **The trap, and it is the part that matters:** closing the job (`SetJobFailed`,
      > `RequeueJob`, `MoveToDLQ`, `AcknowledgeMessage`, `SetJobDone`) is exactly what **has** to
      > happen *after* the cancellation. Running it on the cancelled context would leave the video
      > stuck in `:processing` forever — worse than the original bug. So `processNextMessage` builds
      > a second context with `context.WithoutCancel(ctx)` + 10s (`bookkeepingTimeout`) and uses it
      > for every state write and for the ack. The heavy I/O (download, transcode, uploads, success
      > publish) stays on `processCtx`, which **must** be cancellable.
      > `WithoutCancel` keeps the values — the job logger along — and drops only the cancellation.
      > Rule recorded as **design-decisions #13**.
      >
      > Found along the way: the `//nolint:staticcheck` on `BRPopLPush` was at the end of the line
      > and, with `contextcheck` on, `nolintlint` started reporting it as "unused" **even though it
      > really suppressed SA1019** (checked by removing the directive: SA1019 comes back). Moving
      > the directive to the line above solves it — and it became more readable.
      >
      > Tests: `TestQueueOperations_StopOnCanceledContext` (the 11 public entry points, one per
      > subtest) and `TestRecoverStuckJobs_StopsOnCanceledContext`. `queue` coverage
      > **60.3% → 75.8%**. **Verified by mutation:** putting `GetJobState`'s `client.Get` back on
      > `context.Background()` breaks the `GetJobState` subtest.

- [x] ~~`VidroProcessor/minio/client.go:34` — `const token = ""`~~ **RESOLVED** *(2026-10-05,
      `9969632`)* — it became `staticCredentialsSessionToken`, with the why (a session token only
      applies to temporary STS credentials). Original text: `const token = "" // TODO: Ver se
      precisa adicionar esse token`. **It is the only TODO/FIXME/HACK/BUG marker in all the code of
      the three repos** (sweep over `.cs`, `.ts`, `.tsx`, `.go`, `.json`, `.css`) — the code is
      clean in that respect; the recorded bugs were only in the docs.

---

## Suggested order

*(Revised on 2026-10-06, after the rest of "Flow reliability".)* Already closed: P-OPT1, P-PERF6
(including with a real video), every finding of waves B and C, video forms, P0, P0.1, P1, the
combined queue of 2026-09-07, SEO + language, P-PERF1 to P-PERF5, testable worker loop,
`channels`/`playlists` tests, the end-to-end flow doc, and the whole "Flow reliability" block (only
the breaker decision on `AcknowledgeMessage`/`RequeueJob` remains).

1. ~~**`contracts/job-state.json`**~~ ✅ *(2026-10-12)* — the job envelope, `job:<id>` key and
   queue name are locked by golden on both sides. The next contract gap is the MinIO path layout.
2. **OpenAPI-generated types** (P2, partial) — a lot of work for an incremental gain.
3. Bigger, for later: E2E upload → play (P2), history/notifications/captions (P5), observability
   rung 4 (trace collector).
