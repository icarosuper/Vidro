# Conventions — VidroProcessor

Rules when write/modify Go code. New pipeline step, MinIO/Redis call, or config knob — read first.

Code language and the readability rules that apply to all three services — named variable over inline
expression, extract complex logic into a named function, names that say *what*, three-line ternaries —
live once in the monorepo root [`../../../CLAUDE.md`](../../../CLAUDE.md). **They apply here too.**
This file is Go-specific.

## Formatting

- **`golangci-lint` is the gate**: `.golangci.yml` turns the rules below into build failures — `errorlint` (`%w`, never `%v`), `nilerr`, `forbidigo` (`os.Getenv` only in `config/config.go`, `time.Now` out of the pipeline steps), `depguard` (zerolog in the worker, a pipeline step never importing MinIO/Redis), `godox` (FIXME/HACK/XXX banned, **TODO allowed**), `noctx`, `bodyclose`. Run `golangci-lint fmt && golangci-lint run` before committing; the formatters are `gofumpt` + `goimports` (stricter than `gofmt`), and CI runs the same config. `contextcheck` is **on**: every public function of `queue/` and `minio/` takes a `context.Context` as its first parameter and hands it to Redis/MinIO — see [design-decisions.md #13](design-decisions.md#13-job-context-cancels-the-work-bookkeeping-runs-on-a-context-that-cannot-be-canceled) for the split between the cancellable job context and the bookkeeping one that survives it.
- Package names: lower-case, short (`queue`, `minio`, `metrics`, `processor`).

## Logging

- Use `zerolog`. No stdlib `log` in worker code (exception: `config/config.go` startup).
- **Inside a job, log through the context, never the global logger.** `processNextMessage`
  (`main.go`) builds the job logger once and injects it with `jobLogger.WithContext(ctx)`;
  everything downstream reads it back, so `videoID` and `workerID` land on every line without
  being threaded through any signature:

  ```go
  // main.go — the frame, once per job. correlationID joins the frame when the job envelope
  // carries one (the API stamps it); a job without one logs without the field.
  jobLogger := log.With().Int("workerID", workerID).Str("videoID", videoID).Logger()
  ctx = jobLogger.WithContext(ctx)

  // pipeline steps — anywhere a ctx is in scope
  zerolog.Ctx(ctx).Info().Msg("Step 3/7: Transcoding video")
  ```

  This is what the runbook's `grep <videoId>` relies on
  ([`../docs/troubleshooting-stuck-video.md`](../../../docs/troubleshooting-stuck-video.md), step 4):
  with `WORKER_COUNT > 1`, a bare `Step 3/7` line from two concurrent jobs is indistinguishable.
  `main.go` sets `zerolog.DefaultContextLogger`, so a call site reached without an injected
  logger still logs instead of going silent.
- The global `github.com/rs/zerolog/log` is for **startup and out-of-job** code only:
  `ResolveVideoEncoder`, the HTTP/health handlers, the recovery ticker.
- Prefer structured fields over interpolation: `Str`/`Int`/`Err`, never `fmt.Sprintf` into `Msg`.
- **Output format is decided by the destination, not by a flag**: `logWriter()` in `main.go`
  returns zerolog's `ConsoleWriter` when stderr is a terminal and raw JSON otherwise. Under
  Docker the output has to stay JSON — Promtail ships it to Loki, and a colorized console line
  is only filterable by substring there.

- Log levels:
  - `Info` — lifecycle events, step boundaries.
  - `Warn` — recoverable problems (non-critical step fail, webhook fail, circuit breaker trip, orphan).
  - `Error` — job-level or health check failures needing operator.
  - `Fatal` — startup only. Never inside job.

## Error handling

- Wrap with `fmt.Errorf("context: %w", err)`. Not `%v`.
- Short imperative prefix: `"failed to download video: %w"`, `"failed to serialize job state: %w"`.
- Critical step errors bubble up, abort job. Non-critical: log, swallow.

## Critical vs non-critical pipeline steps

Most important rule in `internal/processor`:

- **Critical** (validate, transcode): return error → `ProcessVideo` aborts, job marked failed/retried.
- **Non-critical** (analyze, thumbnails, audio, preview, streaming): log `Warn`, return error — orchestrator (`runNonCriticalStepsSequential` / `runNonCriticalStepsParallel`, both fed by the `nonCriticalSteps` list) swallows. `ProcessingResult` path set only on success; upload code skips missing artifacts.
- New step: decide category upfront, add it to `nonCriticalSteps`. Never let non-critical step fail pipeline.

## Configuration

- All runtime config in `config/config.go` as `Config` struct, loaded via `caarlos0/env/v10`.
- **Required** vars: `notEmpty` tag — `env.Parse` refuses start without them.
- **Optional** vars: `envDefault:"..."`. Always sensible dev default.
- Mirror every new env var in `.env-example`.
- No `os.Getenv` outside `config/config.go`. Use `Config` struct.

## Metrics

- Declare new metrics in `metrics/metrics.go` with `promauto`.
- Durations: histograms (`video_processing_step_duration_seconds` pattern), not gauges.
- Labels: `status` (success/error), `step` (fixed set) ok. Never label by `videoID` or arbitrary user input.

## Tracing

- All pipeline steps via `runStep` — opens `step/<name>` span, records errors. No direct `telemetry.Tracer().Start` inside step.
- Root span `process_job` opened in `main.go`; children in `processor.ProcessVideo` and `runStep`. Pass `ctx` through.

## External calls

- All MinIO/Redis calls via circuit breakers (`circuitbreaker.MinIO.Execute`, `circuitbreaker.Redis.Execute`). New functions: wrap same as existing.
- No blocking external calls without context/timeout. Exception: `ConsumeMessage` (`BRPOPLPUSH` blocking timeout 0) — cancellation via shutdown context.

## Tests

- Unit tests: `*_test.go` same package.
- Integration tests: `test/integration/`, need a running Docker daemon — they spin up Redis + MinIO with testcontainers (`setup_test.go`). Slow; run `-timeout 10m`.
- Tests using `ffmpeg`/`ffprobe`: must skip when binaries missing — use `GenerateTestVideo` from `test_helpers.go`.
- No mocking MinIO/Redis in integration tests. Test real contract.

## File layout

- New pipeline steps: `internal/processor/processor-steps/<name>.go` + `<name>_test.go`. Register in the `nonCriticalSteps` list in `processor.go` — both orchestrators read it.
- New external-service clients: own top-level package (`queue`, `minio`, ...), not under `internal/`.
- Shared internal helpers (webhook, circuitbreaker, telemetry): `internal/`.

## Shared contract with VidroApi

The rule — both sides change **in the same commit** — is in the root
[`../../../CLAUDE.md`](../../../CLAUDE.md). What the contract *is*, on this side: queue names, MinIO
paths and the webhook payload, spelled out in
[design-decisions.md #11](design-decisions.md#11-single-bucket-path-based-namespacing) and
[#10](design-decisions.md#10-webhook-contract-uses-camelcase-to-match-the-net-api).

## Checklist — adding a pipeline step

The orchestrator does not discover steps; every point below is a place the step has to be wired by
hand, and a step wired into only some of them fails silently or hangs.

1. **Decide critical vs non-critical first** — critical (`validate`, `transcode`) aborts the job on
   error; non-critical logs `Warn` and is swallowed. Changing the existing classification is a
   product decision — talk to VidroApi first ([#3](design-decisions.md#3-critical-vs-non-critical-pipeline-steps)).
2. Create `internal/processor/processor-steps/<name>.go` + `<name>_test.go`. The test must skip when
   `ffmpeg`/`ffprobe` is missing (`GenerateTestVideo` from `test_helpers.go`).
3. Add a `stepTimeout<Name>` constant in `processor.go`. It is scaled by `Options.step()` — never
   read a raw duration inside the step.
4. Register the step in the `nonCriticalSteps` list — **one** place, read by both
   `runNonCriticalStepsSequential` and `runNonCriticalStepsParallel`, so a step can no longer be
   wired into only one of them. Until 2026-09-07 the two orchestrators carried their own copy of
   the list, and a step wired into only one ran only under one value of
   `PARALLEL_NON_CRITICAL_STEPS`. `orchestration_test.go` asserts the list is exactly the four
   post-transcode steps, in order.
5. Run it through `runStep` so it gets its `step/<name>` span and error recording. No direct
   `telemetry.Tracer().Start` inside a step.
6. Wrap every MinIO/Redis call in the matching circuit breaker.
7. Producing an artifact: add the path to `ProcessingResult`, upload it, add the field to the webhook
   payload — then tell VidroApi, because the payload is shared contract.
8. `JobBudget` sums every step timeout, so the new constant widens the whole-job budget by itself.
   Check `internal/processor/timeout_test.go` still passes — it is what asserts the budget covers the
   steps ([#4](design-decisions.md#4-whole-job-timeout-derived-from-the-step-timeouts)).
9. `go test ./...`, then update `features-index.md` and `architecture.md`.

## Checklist — adding an env var

1. Add the field to `Config` in `config/config.go`: `notEmpty` if required, `envDefault:"..."` if
   optional. Never `os.Getenv` outside this file.
2. Mirror it in `.env-example` — commented out when it has a default, uncommented when required.
3. Add a row to [`config.md`](config.md), with the default **and why the default is that value**.
4. If it belongs in the running stack, add it to the monorepo root `../../../docker-compose.yml` (service `worker`).
5. `go test ./...`.
