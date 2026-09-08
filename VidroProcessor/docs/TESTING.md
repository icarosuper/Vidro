# Testing - VidroProcessor

## Current Coverage

Measured with `go test ./... -cover` on 2026-09-07, with ffmpeg installed — without it the
pipeline-step tests skip themselves and the number reads lower.

| Package | Coverage | Type |
|---|---|---|
| `internal/webhook` | 96.8% | Unit |
| `internal/telemetry` | 91.3% | Unit |
| `internal/processor/processor-steps` | 67.6% | Unit |
| `queue` | 60.3% | Unit |
| `internal/circuitbreaker` | 33.3% | Unit |
| `internal/processor` | 11.1% | Unit (`JobBudget` + worker count only) |
| `main` | 11.1% | Unit (webhook contract only) |
| `minio` | 4.4% | Unit |
| `metrics` | — (no statements) | Unit |
| `test/integration` | — | Integration |

**No tests**: `config`

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
`client`/`cfg` globals at the fake and call `recoverStuckJobs` directly instead of waiting
on the one-minute ticker.

- `TestSetJobFailed_IncrementsRetryCountAndPersists`
- `TestSetJobFailed_WithoutExistingState`
- `TestShouldRetry_Boundary` — initial attempt + `MaxJobRetries` retries
- `TestPublishJob_QueuesAndRecordsPending`
- `TestConsumeMessage_MovesJobToProcessing` — the in-flight guarantee
- `TestAcknowledgeMessage_RemovesOneOccurrence`
- `TestRequeueJob_BackToRequestQueueAsPending`
- `TestMoveToDLQ_LandsInDeadQueueOnly`
- `TestPublishSuccessMessage`
- `TestRecoverStuckJobs_RequeuesOrphan`
- `TestRecoverStuckJobs_ExhaustedOrphanGoesToDLQ`
- `TestRecoverStuckJobs_LeavesHealthyAndUnknownJobsAlone` — fresh job, finished job,
  job whose state expired

### `internal/webhook/webhook_test.go`
- `TestNotify_Success`
- `TestNotify_ContentTypeJSON`
- `TestNotify_WithHMAC_CorrectSignature`
- `TestNotify_NoSecret_NoHeader`
- `TestNotify_RetryOnFailure`
- `TestNotify_ErrorAfter3Attempts`
- `TestNotify_InvalidURL`
- `TestNotify_ServerUnavailable`
- `TestPayload_JSONSerialization`

### `webhook_contract_test.go` (package `main`)
- `TestBuildWebhookPayload_MatchesContractGoldens` — one sub-test per golden in
  `../contracts/`: success with every artifact, success with no optional artifact and no
  metadata block, success without `processedPath`, permanent failure.
  The goldens are shared with VidroApi, which POSTs the same files at its own webhook
  endpoint (`VideoProcessedTests.cs`): a renamed field turns both sides red in the same CI
  run. See `../contracts/README.md`.
  **Verified by mutation**: renaming the `json:` tag of `previewPath` fails the test.

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
- `TestPipeline_FullWorkflow` — Redis → download → FFmpeg → upload → success queue
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

## What's Missing

- `config.LoadConfig()` — incl. behavior without `.env`
- `minio.DownloadVideo()` and `UploadVideo()`
- `main.processNextMessage()` — worker orchestration (`buildWebhookPayload` is covered by
  `webhook_contract_test.go`)
- `internal/processor/processor.go` — step orchestration, critical vs non-critical
  classification, per-step timeouts (only `JobBudget` is covered, in `timeout_test.go`)
- `queue`: `InitRedisClient` (`log.Fatal`), `StartRecovery`'s ticker loop, `HealthCheck`,
  `GetQueueSize`, `SetJobProcessing`/`SetJobDone` — the remaining 39.7%
- Transcoding + throughput benchmarks

---

## CI/CD

`.github/workflows/ci.yml` runs on push to `master` and on every PR: `go build`, `go vet`
and `go test ./... -cover -timeout 15m`, with ffmpeg installed so the pipeline-step tests
don't skip themselves.

---

**Last Updated**: 2026-09-07
**Current Coverage**: see the table at the top — measured, not estimated