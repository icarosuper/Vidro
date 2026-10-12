# Cross-service contracts

Golden JSON files for the contracts that cross services. Both sides of each contract test **the
same file**: if one diverges, the other's CI breaks.

| Golden | Crosses |
|---|---|
| `video-processed-*.json` | `VidroProcessor` → `VidroApi` (webhook payload) |
| `job-state.json` | `VidroApi` → `VidroProcessor` (queue name, `job:<id>` key and job envelope) |
| `enums.json` | `VidroApi` → `VidroFront` (the six enums mirrored by hand) |
| `processing-timeout.json` | `VidroProcessor` → `VidroApi` (a job's worst case vs. the `Processing` timeout) |

They exist because both P0s in [`TODO.md`](../TODO.md) were contract divergences between the Go
webhook and the C# handler, and nobody noticed for months. Before the monorepo this would have
required publishing a package; now it is a file. See [`docs/MONOREPO.md`](../docs/MONOREPO.md).

## `video-processed-*.json` — webhook `POST /webhooks/video-processed`

| File | Case | What the API must do |
|---|---|---|
| `video-processed-success-full.json` | the whole pipeline succeeded | video becomes `Ready`, with artifacts and metadata |
| `video-processed-success-minimal.json` | success with no optional artifact and no metadata (every non-critical step failed, `analyze` included) | video becomes `Ready`, empty `thumbnailUrls`, no metadata |
| `video-processed-success-without-processed-path.json` | `success: true` without the transcode output — a success that is not one | video becomes `Failed`, **without** an exception in the handler |
| `video-processed-failure.json` | permanent failure (retries exhausted, job in the DLQ) | video becomes `Failed` |

The `videoId` is the placeholder `11111111-1111-1111-1111-111111111111`. Each side swaps the string
for its test's real id — it is the only field a test may change.

Who tests it:

- `VidroProcessor/webhook_contract_test.go` — `worker.BuildWebhookPayload` from a `queue.JobState`.
- `VidroApi/tests/VidroApi.IntegrationTests/Videos/VideoProcessedTests.cs` — signed POST to the real endpoint.

## `job-state.json` — the job the API publishes

The worker's only input. The API writes the envelope under `stateKey` (`job:<videoId>`, 24h TTL)
and `LPUSH`es the video id onto `queue`. The envelope carries the `callback_url` — lose it and the
worker processes a video it can tell nobody about — and the `correlation_id`, the only telemetry id
that crosses the queue. `created_at`/`updated_at` are fixed in the golden; the API test passes that
instant as "now". The `stateKey` uses the same placeholder `videoId` as the webhook goldens.

Who tests it:

- `VidroApi/tests/VidroApi.UnitTests/Contracts/JobStateContractTests.cs` —
  `RedisJobQueueService.SerializeJobState`/`JobStateKey` (the same functions `PublishJobAsync`
  calls) produce the golden, compared as JSON, not as text; `JobQueueSettings:QueueName` in
  `appsettings.json` equals `queue`. Unit test: no Docker.
- `VidroProcessor/job_state_contract_test.go` — the golden seeded under `stateKey` in miniredis
  comes back through `queue.GetJobState`; decoding with `DisallowUnknownFields` fails on a field the
  worker does not read, and re-encoding fails on one it renamed. The worker's queue name only exists
  in deployment config, so `PROCESSING_REQUEST_QUEUE` is checked in the root `docker-compose.yml`
  and in `VidroProcessor/.env-example` — which is why `processor.yml` also triggers on
  `docker-compose.yml`.

Not covered: the MinIO path layout, which is still discipline.

## `enums.json` — the six enums the front mirrors by hand

Crosses `VidroApi` → `VidroFront`. The API serializes these enums as **integers**, and the front
retypes them in `src/shared/types.ts`: nothing in the type system connects the two sides, so
renaming or renumbering a member would reach the browser as a silently wrong value. `ReactionType`
starts at **1**, not 0 — exactly the kind of detail a refactor takes along without anyone noticing.

The six live in `VidroApi/src/VidroApi.Domain/Enums/`: `VideoStatus`, `VideoVisibility`,
`ReactionType`, `PlaylistVisibility`, `PlaylistScope` and `CommentSortOrder`.

Who tests it:

- `VidroApi/tests/VidroApi.UnitTests/Contracts/EnumContractTests.cs` — reflection over the six
  types. Unit test: no Docker, no container, always runs.
- `VidroFront/src/tests/enum-contract.test.ts` — the six `as const` in `shared/types.ts`.

Both tests also fail if the golden describes an enum their side no longer mirrors — a golden entry
nobody stands behind is a contract nobody checks.

**Why it does not come from OpenAPI:** it would, if the document described the enums. It does
not — see [`VidroApi/openapi/README.md`](../VidroApi/openapi/README.md). `openapi/v1.json` locks
the URL surface; this golden locks the enums.

## `processing-timeout.json` — worker worst case vs. API timeout

The API's reconciliation marks `Failed` a video that has been in `Processing` longer than
`VideoSettings:ProcessingTimeoutMinutes`, and drops a success webhook that arrives later. That
timeout must exceed the worker's worst case with every retry spent — `workerWorstCaseMinutes`, at
scale 1 (`PROCESSING_TIMEOUT_SCALE=1`, `JOB_TIMEOUT` derived):
`(MaxJobRetries + 1) × (OrphanThreshold + RecoveryInterval)` = 4 × (18 + 1 + 1) = **80**.

Who tests it:

- `VidroProcessor/processing_timeout_contract_test.go` — the sum computed from the worker's
  constants matches the golden. Changing retries, budget or the orphan threshold breaks here.
- `VidroApi/tests/VidroApi.UnitTests/Contracts/ProcessingTimeoutContractTests.cs` — the API's
  `ProcessingTimeoutMinutes` in `appsettings.json` stays **above** the golden.

It exists because the old 45 min were calibrated for a single attempt, in a comment no test checked.

---

## Rules for both sides

**Changing a field means changing both sides in the same commit** (rule in the root `CLAUDE.md`).

All three workflows trigger on changes to `contracts/**` (each one's path filter) — otherwise a
golden could change with no CI checking it.

The goldens are pretty-printed for a readable diff. For the webhook, the real body is sent
compact, which is the same JSON.
