# CLAUDE.md — VidroProcessor

Async Go worker for VidroApi. Pull video IDs from Redis queue, download from MinIO, run 7-step FFmpeg pipeline, upload artifacts back. Stateless — scale via more instances same Redis.

Commit convention, branching, edit scope, code language, release/tags and the "bad code nearby" rule
live **once**, in the monorepo root `../CLAUDE.md` (loaded automatically alongside this file). Short
version: all code in English, conventional commits **in Portuguese**, subject line only; straight to
`master` unless the user approves a branch; **never commit without an explicit request**.

## Always-on rules

- **Error wrapping**: `fmt.Errorf("context: %w", err)` — never `%v`.
- **Required config**: required env vars use `notEmpty` (caarlos0/env); optional use `envDefault`. Mirror every new var in `.env-example` **and** in `docs/agents/config.md`.
- **Only create files when necessary.** No `*.md`/README unless asked.

## Running locally

```bash
cp .env-example .env
docker compose -f ../docker-compose.yml up -d redis minio   # just the deps
# or the whole Vidro stack: cd .. && docker compose up -d --build
go run main.go
```

## Running tests

```bash
golangci-lint fmt && golangci-lint run            # the CI gate — run it before committing
go test ./...                                     # unit
go test -v ./test/integration/... -timeout 10m    # integration (needs Docker)
```

Tests shelling to `ffmpeg`/`ffprobe` auto-skip when binaries missing — use `GenerateTestVideo` from `processor-steps/test_helpers.go`.

## Docs pointers — read these when the task calls for it

- **`docs/agents/features-index.md`** — read first when locating code. Maps every feature (worker lifecycle, queue, pipeline steps, MinIO ops, webhook, metrics) to file + canonical MinIO layout + queue names. Update when adding module, pipeline step, or new external contract.

- **`docs/agents/architecture.md`** — read before structural changes, adding pipeline step, or debugging e2e flow. Covers worker lifecycle, queue protocol (main / `:processing` / `:dead` / finished), job state machine, 7-step pipeline orchestration, resilience layers (circuit breakers, orphan recovery, retries).

- **`docs/agents/conventions.md`** — read before writing/modifying Go code. Rules: logging (zerolog, structured fields), error wrapping, critical vs non-critical step classification, config loading, metrics/tracing, circuit-breaker wrapping, test layout. Carries the **new pipeline step** and **new env var** checklists — follow them instead of copying a neighbouring step by eye.

- **`docs/agents/design-decisions.md`** — read before proposing architectural change or questioning *why*. Numbered entries with an index at the top: `BRPOPLPUSH` over Streams, retry→DLQ policy, derived job budget, HLS single-command + fallback, NVENC auto-probe + CPU fallback, soft-archived raws, separate circuit breakers, webhook contract shape. **Read only the entry you were pointed at**, and cite it by anchor (`design-decisions.md #4`), never by line number.

- **`docs/agents/config.md`** — read when touching an env var, a timeout, or a knob in `config/config.go`. Every variable with its default, where it is read, and why the default is what it is.

- **`../docs/troubleshooting-stuck-video.md`** (monorepo root) — follow when a video never leaves `Processing`, or a job lands in the dead-letter queue. Step-by-step across API, Redis, worker and MinIO.

- **`../TODO.md`** (monorepo root, in Portuguese) — read when user asks project status, remaining work, or what to build next. Section **P6** holds this worker's backlog: FFmpeg pipeline performance (P-PERF1..4, P-OPT1) and scalability.

- **`docs/GETTING_STARTED.md`**, **`docs/OBSERVABILITY.md`**, **`docs/TESTING.md`** — guides for setup, observability stack, full integration suite. Read when question is about operating worker, not code.

## Keeping this index healthy

- Add feature → update `features-index.md`.
- Change architecture/cross-cutting flow → update `architecture.md` (+ a new numbered entry in `design-decisions.md` if *why* changes).
- Change coding rule → update `conventions.md`.
- Add/change env var → update `config.md` **and** `.env-example`.
- This file changes only when project structure changes.
