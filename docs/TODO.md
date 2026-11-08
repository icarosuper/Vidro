# TODO — Vidro (VidroApi · VidroFront · VidroProcessor)

Survey done on 2026-08-29. Each item has a file/line reference so the problem does not have to be
rediscovered.

**This file holds open work only.** A closed item is deleted from the body and gets one line in
"Closed so far" below. The full text of everything closed up to 2026-11-05 — P0 (BUG-1, BUG-2),
P0.1 ("Docs that lie"), P1, "Small mess", the "Flow reliability" findings, P-PERF1..6, P-OPT1 — is
in git: `git show c5ee886:docs/TODO.md`. Other docs that cite those items refer to that version.

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
*(2026-10-19)* **`contracts/minio-paths.json`**: object-key layout inside the bucket locked by
golden on both sides (API and worker), mutation-verified.
*(2026-11-05)* **OpenAPI-generated front types**: the API declares its success bodies, `v1.json`
has one schema per slice and enums with values, and the front's request/response types are
generated from it (`schema.d.ts`, CI-gated). Then the domain error body: `ApiErrorResponse` on a
`4XX` range of every `/v1` route, and the front's `ApiError` became its alias.

**Overall state:** the features are done (every Front phase ✅, API plan 100% checked, Processor
~95%), and the shared contract between the three services is fully golden. What is missing is not
features: (a) test gaps (the E2E, front render coverage), (b) worker
scalability (P6) and (c) product (P5).

**Maintaining this file:** an item closed → **delete** it from the body, add a line to "Closed so
far" above and update the "Suggested order" at the end, in the same commit. See the root
`CLAUDE.md`.

---

## P1 — Deploy

Migrations run on startup in every environment (`VidroApi/src/VidroApi.Api/Program.cs`): deploying
the image is the whole deploy.

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

### VidroProcessor

- [ ] **The integration tests skip themselves without Docker/ffmpeg** — in practice they
      rarely run outside CI (which has existed since 2026-08-29 and has Docker + ffmpeg). The 12 in
      `queue/` are the exception: `miniredis` is in-process, they always run.

### VidroFront — façade coverage

- [~] 34 tests in 6 API files with mocked fetch **+ 4 render tests** *(2026-09-13)*:
      `src/tests/comment-list.test.tsx` is the first use of `@testing-library/react`/`jsdom`,
      which were installed and idle. The other 34 components and the 11 routes still have no
      render test — what is missing now is coverage, not harness.
      **The harness cost a new `vitest.config.ts`:** with the TanStack Start plugin in the config,
      React resolves through the `react-server` condition and reaches the test with a null hooks
      dispatcher — every `render` dies in `useState`. The test config loads only `tsconfigPaths` +
      `viteReact`. Pattern recorded in `VidroFront/docs/agents/conventions.md` (section "Component
      tests").

### Across the three

- [ ] No E2E test of the flow that defines the product: upload → processing → play.
      Each repo tests its own edge; the integration between them is not tested.

### Contract between services — unlocked by the monorepo (2026-08-30)

Both P0s of the original survey (closed) were contract divergences between services. While they were three repos,
there was nowhere to put the net that would catch them. Now there is. See `docs/MONOREPO.md`,
section "What the migration unlocks".

- [ ] **Describe the FluentValidation 400 in the OpenAPI document.** The domain error body
      `{ code, message }` is declared (a `4XX` range on every `/v1` route, closed 2026-11-05). The
      FluentValidation 400 `{ errors: [{ field, message }] }` is not: it shares the status with a
      domain `ErrorType.Validation` `{ code, message }`, so OpenAPI cannot describe both without
      `oneOf`, a lie, or unifying the JSON (an API contract change). Do it only when a form needs to
      paint `errors[].field` on inputs — today `ValidationError` is unused and `apiClient` casts
      every error as `ApiError`.

### Flow reliability

- [ ] **Still open (decision):** `AcknowledgeMessage`/`RequeueJob` and the state writes are still
      without a breaker. Only `ConsumeMessage`/`AcknowledgeMessage` go through the Redis circuit
      breaker (`VidroProcessor/internal/circuitbreaker/circuitbreaker.go`); wrapping the rest is
      open because the bookkeeping cannot stop while the breaker is open.

---

## P3 — Frontend UI / UX

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

- [~] **Visual identity "Studio"** — the front was the stock shadcn zinc theme with the system font.
      Direction chosen on 2026-11-07 from a prototype with three switchable variants (parked on the
      `prototype/visual-identity` branch, not merged; screenshots of desktop and mobile were reviewed
      there). Work happens on `feature/visual-identity`.
      *Done:* tokens in `VidroFront/src/styles.css` — violet accent, Manrope for text, JetBrains Mono
      for compact metadata, both self-hosted (`@fontsource-variable/*`). App shell
      (`src/components/shell/`) replacing the `Header`: left rail from `lg`, bottom tab bar below it
      with Upload in the middle, slim top bar with the search (`/` focuses it), account menu on the
      avatar, 24px page gutter so thumbnails never touch the screen edge.
      Class merging moved from `clsx` + `tailwind-merge` to shadcn's `cn` package (the registry
      switched on 2026-11-02 and `shadcn add` now emits `from "cn"`); `src/tests/cn.test.ts` pins the
      merges the components rely on, since `cn` is still 0.x.
      `VideoCard` rebuilt: the title is a stretched link instead of an `<a>` around the card, which
      also ended the nested-`<a>` React warning; mono `views · age`, tags on hover, a featured 2×2
      first card on the home (Trending takes it when the feed is empty — the empty feed is hidden).
      The sidebar collapses (hamburger) and the watch page has a theater mode (button or `t`);
      both are remembered in localStorage and applied before the first paint by a head script.
      The player is capped in height in both modes, like YouTube's.
      Watch page: two columns from `xl` with an "Up next" list (channel first, then trending),
      reactions as one segmented pill, description in a box with mono views/date and `#tags`, a
      skeleton instead of "Loading…".
      *Next, in order:* channel and playlist pages (the channel page keeps its own
      `grid-cols-2` grid, so on a phone its cards are tiny, and its header squeezes the description
      into a narrow column) → loading and empty states.


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

## P6 — VidroProcessor: scalability

Coming from the old `VidroProcessor/docs/roadmap.md`, absorbed here on 2026-08-30. The pipeline
performance work (P-PERF1 to P-PERF6, P-OPT1) is closed: ~26 s per job for 55 s of 1080p, with the
`WORKER_COUNT` curve flat from 1 to 8 workers on one machine. What is left is scaling out.

- [ ] **Auto-scaling:** add workers according to the queue size.
- [ ] **Horizontal scaling:** several worker instances on different machines.
      It is what would make the hardware profile matrix discarded in P-PERF6 start to make sense:
      with a single machine, it does not.
- [ ] **Queue priority:** short videos first, long ones in a separate queue.

---

## Suggested order

*(Revised on 2026-11-05, after the OpenAPI domain error body.)* The shared contract is fully golden
(webhook, job envelope, paths, timeout, enums) and the front's success and domain error types are
generated.

1. **Visual identity "Studio"** (P3) — in progress on `feature/visual-identity`; the channel and playlist pages are next.
2. **E2E upload → play** (P2) — the biggest test gap left.
3. Bigger, for later: history/notifications/captions (P5), observability rung 4 (trace collector),
   worker scaling (P6). The FluentValidation 400 in OpenAPI only when a form needs per-field errors.
