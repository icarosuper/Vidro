# Testing - VidroProcessor

## Current Coverage

Measured with `go test ./... -cover` on 2026-10-05, with ffmpeg installed — without it the
pipeline-step tests skip themselves and the number reads lower.

| Package | Coverage | Type |
|---|---|---|
| `internal/webhook` | 96.8% | Unit |
| `internal/telemetry` | 91.3% | Unit |
| `internal/processor/processor-steps` | 68.0% | Unit |
| `queue` | 75.8% | Unit |
| `internal/circuitbreaker` | 33.3% | Unit |
| `internal/processor` | 78.0% | Unit |
| `internal/worker` | 59.1% | Unit (miniredis + fake MinIO/pipeline) |
| `main` | 0.0% | Unit (hosts the webhook contract test, which exercises `internal/worker`) |
| `minio` | 4.7% | Unit |
| `metrics` | — (no statements) | Unit |
| `test/integration` | — | Integration |

`config` has one test, `TestConfig_RequiresWebhookSecret` (the `notEmpty` tag via `env.Parse`);
`LoadConfig` itself calls `log.Fatalf` and stays untested.

---

## Running Tests

```bash
# All tests
go test ./...

# With coverage
go test ./... -cover

# Verbose output
go test -v ./...

# HTML report
go test ./... -coverprofile=coverage.out
go tool cover -html=coverage.out
```

### By package

```bash
go test -v ./internal/processor/processor-steps/...
go test -v ./metrics/...
go test -v ./test/integration/... -timeout 10m
```

### Integration tests

Need Docker. Auto-skip if Docker absent.

```bash
go test -v ./test/integration/... -timeout 10m
```

---

## Unit Tests

### `validate_test.go`
- `TestValidateVideo_ValidVideo`
- `TestValidateVideo_InvalidVideo`
- `TestValidateVideo_NonExistentFile`
- `TestValidateVideo_EmptyFile`

### `transcode_test.go`
- `TestTranscodeVideo_ValidVideo` — checks output created, non-empty
- `TestTranscodeVideo_InvalidInput`
- `TestTranscodeVideo_NonExistentInput`

### `thumbnail_test.go`
- `TestGenerateThumbnails_ValidVideo` — checks 5 `thumb_00N.jpg` files
- `TestGenerateThumbnails_InvalidVideo`
- `TestGenerateThumbnails_NonExistentVideo`

### `analysis_test.go`
- `TestAnalyzeContent_ValidVideo`
- `TestAnalyzeContent_InvalidVideo`
- `TestAnalyzeContent_NonExistentVideo`

### `metrics_test.go`
- `TestVideosProcessedTotal_Increment`
- `TestProcessingDuration_Observe`
- `TestProcessingStepDuration_MultipleSteps`
- `TestActiveWorkers_SetAndGet`
- `TestQueueSize_SetAndGet`

### `internal/circuitbreaker/circuitbreaker_test.go`
- `TestMinIO_InitialState_Closed`
- `TestRedis_InitialState_Closed`
- `TestCircuitBreaker_OpensAfter5ConsecutiveFailures`
- `TestCircuitBreaker_OpensAfter3ConsecutiveFailures_Redis`
- `TestCircuitBreaker_RejectsCallsWhenOpen`
- `TestCircuitBreaker_DoesNotOpenWithNonConsecutiveFailures`
- `TestCircuitBreaker_ReturnsResultWhenClosed`

### `queue/queue_test.go`

Runs against [miniredis](https://github.com/alicebob/miniredis) — in-process, no Docker,
so these never skip themselves. The tests live inside the package so they can point the
`client`/`cfg` globals at the fake and call `RecoverStuckJobs` directly instead of waiting
on the one-minute ticker.

- `TestSetJobFailed_IncrementsRetryCountAndPersists`
- `TestSetJobDoneAndFailed_WithoutExistingStateRefuseAndCreateNothing` — same rule as `SetJobProcessing`
- `TestGetJobState_DistinguishesMissingFromRedisFailure` — Redis down is not "job missing"
- `TestSetJobProcessing_WithoutExistingStateRefusesAndCreatesNothing` — no state, no callback_url: never recreated
- `TestShouldRetry_Boundary` — initial attempt + `MaxJobRetries` retries
- `TestPublishJob_QueuesAndRecordsPending`
- `TestConsumeMessage_MovesJobToProcessing` — the in-flight guarantee
- `TestAcknowledgeMessage_RemovesOneOccurrence`
- `TestRequeueJob_BackToRequestQueueAsPending`
- `TestMoveToDLQ_LandsInDeadQueueOnly`
- `TestRecoverStuckJobs_RequeuesOrphan`
- `TestRecoverStuckJobs_ExhaustedOrphanGoesToDLQ`
- `TestRecoverStuckJobs_FailedRequeueIsRetriedOnNextSweep` — move fails: state stays `processing`, next sweep requeues
- `TestRecoverStuckJobs_NotifiesOnlyWhenOrphanIsDeadLettered` — the `onDeadLettered` callback fires for the exhausted orphan, not the requeued one
- `TestRecoverStuckJobs_JobWithoutStateGoesToDLQ` — state expired while parked in `:processing`
- `TestRecoverStuckJobs_LeavesHealthyJobsAlone` — fresh job, finished job
- `TestQueueOperations_StopOnCanceledContext` — the 10 public entry points, one canceled
  context each: proves the ctx reaches Redis instead of being swallowed for
  `context.Background()` (see design-decisions #13)
- `TestRecoverStuckJobs_StopsOnCanceledContext` — a shutdown stops the sweep mid-scan

### `internal/webhook/webhook_test.go`
- `TestNotify_Success`
- `TestNotify_ContentTypeJSON`
- `TestNotify_WithHMAC_CorrectSignature`
- `TestNotify_RetryOnFailure`
- `TestNotify_ErrorAfter3Attempts`
- `TestNotify_PermanentClientErrorsAreNotRetried` — 400/401/404 fail on the first attempt
- `TestNotify_TooManyRequestsIsRetried`
- `TestNotify_InvalidURL`
- `TestNotify_ServerUnavailable`
- `TestPayload_JSONSerialization`

### `internal/worker/worker_test.go`

Drives `processNextMessage` — the job loop — against miniredis (through `queue.InitRedisClient`)
with a typed fake for MinIO (`fakeStorage`) and for the FFmpeg pipeline. No Docker, no ffmpeg.
The ack runs in the job goroutine's defers, which can outlive `processNextMessage`, so the tests
poll until `:processing` drains (2s ceiling) instead of asserting right after the return.

- `TestProcessNextMessage_FailedJobIsClosedOut` — the design-decisions #13 regression: a failed
  job leaves `:processing` with `retry_count` incremented, requeued as `pending` while budget is
  left, dead-lettered once it is spent
- `TestProcessNextMessage_CompletedJobIsAcked` — the other half of #13: a successful job is acked
  (`done`, with its artifacts) instead of waiting for orphan recovery
- `TestWorker_CrashMidJobIsRecoveredAndReprocessed` — the queue's failure path end to end: a worker
  dies mid-job (its goroutine never reaches the bookkeeping), the job survives in `:processing`,
  `queue.RecoverStuckJobs` requeues it with `retry_count` 1, and a second worker finishes it

- `TestProcessNextMessage_JobWithoutStateIsDeadLetteredUnprocessed` — `job:<id>` expired or
  deleted: no download, straight to `:dead`, state not recreated without a `callback_url`

Mutation-checked: moving `defer cancelBookkeeping()` back into the parent turns all three red.

### `webhook_contract_test.go` (package `main`)
- `TestBuildWebhookPayload_MatchesContractGoldens` — one sub-test per golden in
  `../contracts/`: success with every artifact, success with no optional artifact and no
  metadata block, success without `processedPath`, permanent failure.
  The goldens are shared with VidroApi, which POSTs the same files at its own webhook
  endpoint (`VideoProcessedTests.cs`): a renamed field turns both sides red in the same CI
  run. See `../contracts/README.md`.
  **Verified by mutation**: renaming the `json:` tag of `previewPath` fails the test.

### `internal/processor/orchestration_test.go`
The orchestrator is a policy, not a transformation — these run with fake steps, no FFmpeg:
- `TestNonCriticalSteps_AreTheFourPostTranscodeSteps` — the list is thumbnails, audio, preview,
  streaming, in order, and `nonCriticalStepCount` matches its length
- `TestNonCriticalSteps_TimeoutsFollowTheScale` — every step timeout goes through `Options.step`,
  so `PROCESSING_TIMEOUT_SCALE` cannot stop applying to one of them
- `TestRunNonCriticalStepsSequential_FailingStepDoesNotStopTheRest`
- `TestRunNonCriticalStepsParallel_FailingStepDoesNotCancelSiblings` — the reason this is a
  `WaitGroup` and not an `errgroup`
- `TestRunNonCriticalStepsParallel_RespectsMaxParallel` — the per-job FFmpeg ceiling (P-PERF4)
- `TestRunStep_ReturnsTheStepError`, `TestRunStep_AppliesTheStepTimeout`,
  `TestRunStep_ParentCancellationReachesTheStep`
- `TestRunStep_RecordsTheStepDuration` — the per-step metric P-PERF6 will read
- `TestProcessVideo_InvalidInputFailsAtValidation` — critical step aborts the job and no
  post-transcode step runs (needs `ffprobe`, skips without it)

**Verified by mutation**: `continue` → `return` in the sequential orchestrator, an unbounded
semaphore in the parallel one, dropping `opts.step()` from one timeout, renaming a step, and
removing the `ProcessingStepDuration` observation each fail a different test.

### `internal/processor/timeout_test.go`
- `TestJobBudgetExceedsStepTimeouts`, `TestTimeoutScale`

### `internal/processor/workers_test.go`
- `DefaultWorkerCount` — the invariant is `workers × parallel steps ≤ cores`

### `internal/telemetry/telemetry_test.go`
- `TestInit_EmptyEndpoint_Noop`
- `TestInit_EmptyEndpoint_InstallsNoop`
- `TestTracer_ReturnsNonNil`
- `TestTracer_CreatesSpan`
- `TestTracerName_Constant`
- `TestInit_InvalidEndpoint_ReturnsError`

---

## Integration Tests (`test/integration/`)

Use **testcontainers-go** — spins real Redis + MinIO.

### `minio_test.go`
- `TestMinIO_BucketOperations`
- `TestMinIO_ObjectUploadDownload`
- `TestMinIO_VideoWorkflow` — raw → processed flow
- `TestMinIO_DownloadToFile`
- `TestMinIO_NonExistentObject`

### Pipeline tests (`pipeline_test.go`)
- `TestPipeline_ValidateStep`
- `TestPipeline_TranscodeStep`
- `TestPipeline_FullWorkflow` — Redis → download → FFmpeg → upload
- `TestPipeline_ThumbnailGeneration`

---

## Test Helpers

### `GenerateTestVideo(t, duration int) string`

Makes test video via FFmpeg (640x480, H.264+AAC, 1000Hz sine). Auto-skips if FFmpeg absent.

```go
videoPath := GenerateTestVideo(t, 5) // 5 seconds
```

### `CreateInvalidFile(t) string`

Creates invalid-content file for error testing.

---

## Requirements

**FFmpeg** required for processing tests:

```bash
# Ubuntu/Debian
sudo apt-get install ffmpeg

# macOS
brew install ffmpeg
```

FFmpeg-dependent tests auto-skip with:
```
FFmpeg is not available - skipping test
```

---

## Benchmarks (`internal/processor/pipeline_bench_test.go`, P-PERF6)

Real FFmpeg, no mocks. Benchmarks never run in `go test ./...` (only with `-bench`); they skip
without `ffmpeg`/`ffprobe`. The clip is generated per run (10s, 1280x720, 30fps, `testsrc` +
`sine`), not versioned.

```bash
go test ./internal/processor -run '^$' -bench . -benchtime 3x -count 3
```

One variable per benchmark: `BenchmarkTranscodeVideo` (a), `BenchmarkNonCriticalSteps`
(b: sequential vs `runNonCriticalStepsParallel` at 1/2/4), `BenchmarkProcessVideo` (c: all
steps vs steps 4-7 skipped, i.e. all `ENABLE_*` off), `BenchmarkConcurrentJobs` (d: a batch of 8
jobs drained by 1/2/4/8 workers). `BENCH_VIDEO=/path/to/video.mp4` swaps the synthetic clip for a
real file.

Measured 2026-10-05 on AMD Ryzen 7 7800X3D (8 cores / 16 threads, `NumCPU`=16), ffmpeg n9.0.2,
CPU encoder (libx264, no NVENC), idle host, `-benchtime 3x -count 3`:

| Benchmark | Runs (s/op) | Mean |
|---|---|---|
| (a) TranscodeVideo | 0.442 / 0.452 / 0.455 | 0.45 s |
| (b) steps 4-7 sequential | 1.487 / 1.466 / 1.479 | 1.48 s |
| (b) parallel, max=1 | 1.479 / 1.459 / 1.484 | 1.47 s |
| (b) parallel, max=2 | 0.998 / 1.004 / 0.992 | 1.00 s |
| (b) parallel, max=4 | 0.969 / 0.968 / 0.970 | 0.97 s |
| (c) ProcessVideo, all steps (parallel 4) | 1.477 / 1.470 / 1.461 | 1.47 s |
| (c) ProcessVideo, steps 4-7 skipped | 0.498 / 0.497 / 0.498 | 0.50 s |
| (d) 8 jobs, 1 worker | 11.58 / 11.68 / 11.80 | 11.7 s |
| (d) 8 jobs, 2 workers | 9.95 / 9.84 / 9.74 | 9.8 s |
| (d) 8 jobs, 4 workers | 9.73 / 9.35 / 9.39 | 9.5 s |
| (d) 8 jobs, 8 workers | 9.38 / 9.35 / 9.42 | 9.4 s |

(d) measured 2026-10-06, same host, `-benchtime 2x -count 3`.

(d) again on a real video (`BENCH_VIDEO`: 54.5 s, 1080p30 HEVC phone footage, 100 MB), 2026-10-08,
same host, `-count 1`:

| Benchmark | s/op | per job |
|---|---|---|
| (d) 8 jobs, 1 worker | 204.9 s | 25.6 s |
| (d) 8 jobs, 2 workers | 201.8 s | 25.2 s |
| (d) 8 jobs, 4 workers | 207.1 s | 25.9 s |
| (d) 8 jobs, 8 workers | 207.2 s | 25.9 s |

What this confirms and what it does not:

- **Confirmed (P-PERF3):** parallel steps 4-7 take 0.97 s vs 1.48 s sequential (~1.5x); max=1
  equals sequential, as it should. Most of the gain is already at max=2; 2 to 4 adds ~3%. On
  this clip the wall time is probably bounded by the slowest single step (inference, not
  measured per step), so the ceiling of 4 buys little here.
- **Confirmed (P-OPT1):** skipping steps 4-7 cuts the pipeline from 1.47 s to 0.50 s (~66%).
  Steps 4-7 are ~2/3 of the work on this clip.
- **Not measured:** P-PERF1 (HLS single command) and P-PERF2 (`fast` vs `medium`): there is no
  before/after, both are already the code. The synthetic numbers are 10 s of 720p `testsrc`, which
  encodes far more easily than camera footage, so do not extrapolate absolute times from them.
- **Plausible ("~2-3 min" for 500 MB):** the real video takes ~26 s per job for 55 s of 1080p;
  500 MB at the same bitrate is ~275 s of footage, so ~2 min. Linear extrapolation, not a measurement.
- **Confirmed (P-PERF5), on the synthetic clip:** throughput goes 11.7 s -> 9.8 s going from 1 to 2
  workers (-16%), then flattens (9.5 s at 4, 9.4 s at 8): the knee is at 2, exactly what
  `DefaultWorkerCount` gives on this host (16 threads / ~8 FFmpeg processes per job). More workers
  than that buy nothing and only add memory pressure.
- **On a real video the curve is flat:** 1 to 8 workers all land within ~2.5% (one run each, so
  noise). A single job already saturates the 16 threads; the 1 -> 2 gain on the synthetic clip was
  process startup, not encode. The default of 2 neither helps nor hurts on long footage, and still
  helps on short clips.

---

## What's Missing

- `config.LoadConfig()` — incl. behavior without `.env`
- `minio.DownloadVideo()` and `UploadVideo()`
- `internal/processor/processor.go` — the FFmpeg happy path of `ProcessVideo` (steps 2 and 3
  onwards with a real video). The orchestration policy around it is covered by
  `orchestration_test.go`
- `queue`: `InitRedisClient` (`log.Fatal`) and `StartRecovery`'s ticker loop — the remaining 24.2%
- `internal/worker`: the bare `Run` loop, webhook dispatch and the optional-artifact uploads
- Throughput under real load (benchmarks exist, but on a 10s synthetic clip — see "Benchmarks")

---

## CI/CD

`.github/workflows/processor.yml` (monorepo root, filtered by path) runs on push to `master`
and on every PR: `go build`, `golangci-lint` and `go test ./... -cover -timeout 15m`, with
ffmpeg installed so the pipeline-step tests don't skip themselves. The lint step replaced the
separate `gofmt` and `go vet` steps — both are inside the golangci-lint config now.

---

**Last Updated**: 2026-10-05
**Current Coverage**: see the table at the top — measured, not estimated