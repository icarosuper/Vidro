# Observability — what exists, what is missing, in what order to close it

Survey done on **2026-09-09** across the three services and the `docker-compose.yml` stack, trying
to answer a single question: **can you reconstruct a video's life, end to end, without opening the
code?**

Short answer: no. All the infrastructure is already up — Loki, Promtail, Prometheus, Grafana,
Serilog, zerolog, Prometheus metrics in the worker and OpenTelemetry installed. What is missing is
the common thread: no identifier crosses API → Redis → worker → webhook in a queryable way.

> **Today (2026-10-05) the answer is yes**, through the `correlation_id`: rungs 0–3 closed F1, F2,
> F3, F5 and F7. Still open are F4 (spans without a collector), F6 (Loki sink only in Development)
> and F7.1. The "What already exists" section below was updated; the gaps keep the original
> diagnosis.

Paths cited here are relative to the **monorepo root**, not to this folder.

---

## Concepts, the minimum to read the rest

**The three signals** answer different questions, and the difference that matters is cardinality:

| Signal | Answers | Cardinality | Where it is here |
|---|---|---|---|
| Metric | how many, how fast, is it getting worse | low — a label is a closed set | `VidroProcessor/metrics/metrics.go` → Prometheus |
| Log | what happened in this specific case | high — a field takes any value | Serilog / zerolog → Loki |
| Trace | where the time went, across services | high | `VidroProcessor/internal/telemetry/` → **nowhere** |

**`videoId` never becomes a metric label.** Each video would create a new time series. IDs go in
logs and traces; a metric only gets closed dimensions (`status`, `step`) — which is exactly what
`metrics.go` already does.

**Three kinds of identifier, with different lifetimes:**

- **Correlation ID** — born at the edge (or comes from the client), lives for one HTTP request.
  Here: `X-Correlation-ID`, in `VidroApi/src/VidroApi.Api/Middleware/CorrelationIdMiddleware.cs`.
- **Trace ID / Span ID** — W3C standard (`traceparent`), propagated between processes; the trace
  is the tree of spans, each with a parent and a duration.
- **Business ID** — `videoId`. It is not telemetry, but in this project it is the only one that
  survives retry, dead-letter and reconciliation. A job requeued 40 min later is another trace, same
  `videoId` — so **`videoId` is the domain's primary correlation key**, and the trace is the detail
  of one attempt.

**Context propagation** is the only concept that costs work: the ID has to get into the *envelope*
of each hop. HTTP has headers; **a queue does not** — whoever publishes puts it in the payload,
whoever consumes extracts it. That is where the chain broke here (F3); since 2026-09-13 the
`correlation_id` travels in the `JobState` and comes back in the webhook header.

---

## What already exists

### Infra (`docker-compose.yml`)

Loki (`:3100`), Promtail reading the Docker socket (every container), Prometheus (`:9090`),
Grafana (`:3001`) with Loki + Prometheus datasources provisioned
(`VidroProcessor/grafana/provisioning/datasources/`) and a worker dashboard
(`.../dashboards/video-processor.json`).

### `VidroApi`

- `Middleware/CorrelationIdMiddleware.cs` — accepts an incoming `X-Correlation-ID` or generates
  one, returns it in the response and pushes it into Serilog's `LogContext`.
- `Common/Logging/ProcessingLogScope.cs` + `LoggingDefaults.cs` — scoped `CorrelationId`,
  `ProcessType`, `MethodName` properties.
- `Application/Behaviors/RequestLoggingPipelineBehavior.cs` — logs start, success, domain error and
  exception of every feature, with request and response serialized.
- `Application/Common/Logging/JsonLogSerializer.cs` — redaction by attribute (`[LogIgnore]`,
  `[LogMask]`). **PII/secret protection in logs is already solved**, and any new sensitive field
  only needs the attribute.
- Loki sink configured — only in `appsettings.Development.json` (see F6).
- Native .NET metrics (HTTP, Kestrel, rate limiting, Npgsql) on `/metrics`, scraped by Prometheus's
  `vidro-api` job (rung 2). No Grafana dashboard yet.
- Writes `correlation_id` into Redis's `job:<videoId>` (rung 3).

### `VidroProcessor`

- Real Prometheus metrics in `metrics/metrics.go`: `videos_processed_total{status}`,
  `video_processing_duration_seconds`, `video_processing_step_duration_seconds{step}`,
  `active_workers`, `queue_size`, `video_size_bytes`. Exposed in `main.go:191` (`/metrics`),
  scraped every 15s.
- Full OpenTelemetry tracing in `internal/telemetry/telemetry.go` — OTLP/HTTP exporter, no-op if
  `OTEL_ENDPOINT` is empty. Root span `process_job` with the `video.id` attribute in `main.go:266`.
- JSON log (zerolog) with `videoID`, `workerID` and `correlationID` (read from the job's
  `correlation_id`; the recovery and webhook delivery lines too, via `JobState.Logger`); the
  webhook returns the same value in `X-Correlation-ID` (rungs 0 and 3).

### `VidroFront`

`shared/lib/correlation-id.ts` generates an `X-Correlation-ID` per request (browser and SSR), and
`RouteErrorFallback` shows the id for the user to quote to support (rung 1). No browser error sink.

---

## Gaps

Numbered by pain, not by effort.

### ~~F1~~ ✅ — The runbook promises a field the code does not write *(resolved on 2026-09-09)*

`docs/troubleshooting-stuck-video.md:95` states *"Every pipeline log line carries `videoID`"* and
step 4 says `docker compose logs worker | grep <videoId>`.

But the pipeline steps use the global logger, with no field at all:
`VidroProcessor/internal/processor/processor.go` (`ProcessVideo`, steps 1-3 via `stepMessage`) and
the two non-critical step orchestrators (`runNonCriticalStepsSequential` /
`runNonCriticalStepsParallel`). The `videoID` only appeared on the frame lines
(`processNextMessage`), not in the pipeline. The `N/M` in `Step N/M` counts only the enabled steps
(`ENABLE_*` flags), so `M` is not always 7.

**Consequence:** with `WORKER_COUNT > 1`, `Step 3/N: Transcoding video` from two concurrent videos
is indistinguishable. Step 4 of the runbook fails exactly under the load where it is needed.

> **RESOLVED** — rung 0. `processNextMessage` builds the `jobLogger` once (`videoID` +
> `workerID`) and injects it with `jobLogger.WithContext(ctx)`; the two orchestrators and the steps
> that already received `ctx` (`analysis.go`, `transcode.go`, `streaming.go`) read it back with
> `zerolog.Ctx(ctx)`. **No signature changed** and no dependency came in.
> `zerolog.DefaultContextLogger` points at the global logger, so a call site reached without an
> injected logger keeps logging instead of going silent — it is the default `zerolog.Ctx` uses when
> it finds nothing in the context.
> Out of coverage on purpose: `video_encoder.go` (runs at startup, before any job exists) and the
> health/metrics handlers.

### ~~F2~~ ✅ — The worker logs human text, not JSON *(resolved on 2026-09-09)*

`VidroProcessor/main.go:55`: `log.Logger = log.Output(zerolog.ConsoleWriter{Out: os.Stderr})`.
Promtail delivers it to Loki, but as a text line with ANSI escapes — no fields. Even where the
`videoID` exists, you cannot filter on it in Loki, only by substring.

> **RESOLVED** — rung 0. `logWriter()` returns `ConsoleWriter` when stderr is a TTY and raw
> `os.Stderr` (JSON) otherwise. **No new knob:** the destination decides. Running the worker in the
> terminal stays readable; under `docker compose` the output becomes JSON and the runbook's search
> becomes `| json | videoID="..."` in Loki.

### ~~F3~~ ✅ — The correlation ID dies at the queue boundary *(resolved on 2026-09-13)*

`VidroApi/src/VidroApi.Infrastructure/Services/RedisJobQueueService.cs` writes `job:<videoId>` with
`status`, `callback_url`, `retry_count`, `created_at`, `updated_at` and `LPUSH`es the bare
`videoId`. `VidroProcessor/queue/job.go` (`JobState`) has the same fields and no telemetry one. The
webhook payload (`contracts/*.json`) does not either.

**Consequence:** API and worker never share a telemetry identifier. The `videoId` is the only link
— and it works (it is what the runbook uses), but it does not carry the correlation ID of the user
request that started the upload.

> **RESOLVED** — rung 3, below. The field is `correlation_id` in the `JobState`, and the webhook
> returns the same value in the `X-Correlation-ID` header.

### F4 — The spans go into the void

`OTEL_ENDPOINT` is not set in `docker-compose.yml` and there is no collector in the stack (neither
Tempo nor Jaeger). All of `internal/telemetry` runs in no-op mode — by design, and documented that
way in `VidroProcessor/docs/agents/config.md`. The API has no OpenTelemetry at all: the `.csproj`
only brings Serilog + enrichers.

### ~~F5~~ ✅ — Prometheus only scrapes the worker *(resolved on 2026-09-13)*

`VidroProcessor/prometheus/prometheus.yml` has a single `scrape_config`, target `worker:8080`. The
API exposes no metric: HTTP error rate per endpoint, latency, the Npgsql pool and the rate limit
state are invisible. The front has no metric either, which is acceptable for now.

> **RESOLVED** — rung 2, below. The front still has no metric, on purpose.

### F6 — The Loki sink only exists in Development

`VidroApi/src/VidroApi.Api/appsettings.json` (base) only has the `Enrich` block; `WriteTo` with
Console + GrafanaLoki is only in `appsettings.Development.json`. In compose, Promtail saves the day
(it reads the container's stdout), so the gap only shows in a deploy outside Docker — but then the
API sends logs nowhere.

### ~~F7~~ ✅ — The front is blind *(resolved on 2026-09-09)*

`VidroFront/src/shared/lib/api-client.ts` sets `Content-Type` and `Authorization` and nothing
else: it neither generates nor propagates `X-Correlation-ID`, even though the API **already accepts
the incoming header**. The error fallback (commit `5537d16`) shows the message without any ID the
user could quote to support.

> **RESOLVED** — rung 1. `shared/lib/correlation-id.ts` generates one id per request; the
> `apiClient` sends it in `X-Correlation-ID`, keeps it in `ApiClientError.correlationId` and
> `RouteErrorFallback` shows it ("Reference for support"). **One-sided change:** the API already
> accepted the header and the CORS policy uses `AllowAnyHeader()`, so nothing had to change there.
> **The two `features/*/server.ts` came along** — they are the SSR path, exactly where the error
> screen shows up; doing only the `apiClient` would have covered the browser and not the loader.
> The presigned upload is left out on purpose: it goes to MinIO, not to the API.
> `crypto.randomUUID` is `undefined` outside a secure context, so there is a fallback — a throw
> there would take down every request, not one log line.

### F7.1 — the front does not *read* the id back yet

`RouteErrorFallback` shows the id the **front generated**, and the API reuses it, so in practice it
is the same value. The uncovered path is the response header (`Access-Control-Expose-Headers` does
not list `X-Correlation-ID`): if the API ever stops reusing the incoming id, the front would show a
value that exists in no log. As long as the middleware reuses it, the diff is not worth it.

---

## Suggested order

Each rung is useful on its own. The order is by return on diff, not by completeness.

### ~~Rung 0~~ ✅ — contextual logger in the worker → closed F1 and F2 *(2026-09-09)*

In `processNextMessage` (`VidroProcessor/main.go:200`), build
`jobLogger := log.With().Str("videoID", videoID).Int("workerID", workerID).Logger()`, inject it into
the context (`jobLogger.WithContext(ctx)`) and replace `log.Info()` with `zerolog.Ctx(ctx).Info()`
in the steps. `ProcessVideo` already receives `ctx`, so **no signature changes**. A native zerolog
feature, no new dependency.

Along with it, in `main.go:55`: `ConsoleWriter` only when the output is a TTY (or behind a dev env
var), JSON otherwise. Then the runbook's `grep` becomes a field query in Loki, and the runbook's
text becomes true.

> **DONE.** Locked by `TestOrchestrators_LogLinesCarryTheJobFields`
> (`VidroProcessor/internal/processor/orchestration_test.go`): the two orchestrators run with a
> buffer logger in the context and every emitted line must carry the `videoID`.
> **Verified by mutation:** replacing `zerolog.Ctx(ctx)` with `zerolog.Ctx(context.Background())`
> in the sequential orchestrator empties the output and breaks the test. Docs updated along:
> `troubleshooting-stuck-video.md` (step 4, with the LogQL query), `conventions.md` (Logging
> section, with the rule of logging through the context inside the job) and
> `VidroProcessor/docs/OBSERVABILITY.md`.

### ~~Rung 1~~ ✅ — the front generates the ID → closed F7 *(2026-09-09)*

`crypto.randomUUID()` per request in `api-client.ts`, `X-Correlation-ID` header, keep the last one
in `ApiClientError` and show it on the error screen. The API already honors the header — it is a
one-sided change. The biggest return per diff line in the whole document.

> **DONE.** Locked by two tests in `VidroFront/src/tests/api-client.test.ts`: the header goes out
> **different on each request**, and the `ApiClientError` id is the same one that was sent.
> **Verified by mutation:** removing the header line from the `apiClient` breaks both.
> Docs updated along: `VidroFront/docs/agents/architecture.md` (`apiClient` responsibilities +
> Correlation ID section) and `features-index.md`.

### ~~Rung 2~~ ✅ — API metrics → closed F5 *(2026-09-13)*

.NET already emits native meters (`Microsoft.AspNetCore.Hosting`, `System.Net.Http`, Npgsql). What
is missing is the exporter (`OpenTelemetry.Exporter.Prometheus.AspNetCore`) and a second job in
`prometheus.yml` pointing at `api:5000`. **Do not write a counter by hand before that** — what comes
for free covers rate, errors and latency.

> **DONE.** `AddOpenTelemetry().WithMetrics(...)` in `Program.cs` with four meters
> (`Microsoft.AspNetCore.Hosting`, `.Server.Kestrel`, `.RateLimiting`, `Npgsql`) +
> `MapPrometheusScrapingEndpoint()`, and the `vidro-api` job pointing at `api:5000`. **Zero
> hand-written counters**, as the rung said. What shows up on `/metrics`:
> `http_server_request_duration_seconds` (histogram, with `http_route` and status),
> `http_server_active_requests`, `db_client_connection_count{state}`, `db_client_connection_max`,
> `db_client_operation_duration_seconds` and Npgsql's byte counters.
>
> **Two things found along the way:**
> 1. **The Npgsql pool name becomes a label.** The default is the connection string — Npgsql
>    removes the password (checked by hand), but host, port, database and user stay, on an
>    unauthenticated endpoint, and the value changes per environment. `AddInfrastructure` now
>    builds an `NpgsqlDataSource` with `Name = "vidroapi"`. Recorded as the API's
>    **design-decisions #12**.
> 2. **`/metrics` had to leave the request log**, as `/health` already had: with a 15s scrape, each
>    scrape would be a line.
>
> Tests: `tests/VidroApi.IntegrationTests/Common/MetricsEndpointTests.cs` — the scrape carries the
> request histogram and the pool label is `vidroapi`, not the connection string.
> **Verified by mutation:** changing the data source's `Name` turns the second test red.
>
> **Confirmed on the real stack** *(2026-09-13, along with the load test)*: Prometheus shows
> `up{job="vidro-api"} 1`, the p95 per route comes out split by `http_route`, the Npgsql pool shows
> in `db_client_connection_count{state}`, and `aspnetcore_rate_limiting_requests_total` separates
> `acquired` from `endpoint_limiter` — it was the series missing for the limiter to stop being a
> promise. See [`loadtest/README.md`](../loadtest/README.md).
>
> **Still open:** there is no API dashboard in Grafana — the provisioned one is only the worker's.

### ~~Rung 3~~ ✅ — identifier in the envelope → closed F3 *(2026-09-13)*

A trace field in the `jobState` (the API writes it, the worker extracts it with
`propagation.TraceContext().Extract`) and a header on the webhook POST.

**This is a shared contract:** `RedisJobQueueService.cs`, `queue/job.go` and
`contracts/job-state.json` change in the same commit (plus the `video-processed-*.json` goldens if
the field touches the webhook payload). See "Shared contract" in the root `CLAUDE.md`.

> **DONE — with a deliberate change to the plan: the field is `correlation_id`, not
> `traceparent`.**
>
> The reason is that `traceparent` today would be **fiction**. The API has no tracing: what came
> into it on 2026-09-13 was metrics (rung 2), with no `TracerProvider`, so `Activity.Current` is
> null and a `traceparent` written by it would point at a trace that exists nowhere — an ID that
> looks like the W3C standard with no trace behind it. The `correlation_id` already exists, is
> already what the error screen shows the user ("Reference for support", F7) and is already what
> the API stamps on every log line. **`traceparent` comes back in rung 4**, with the collector:
> then it points at real spans, and the envelope field carries both.
>
> **What changed on both sides, in the same commit:**
> - `RedisJobQueueService.PublishJobAsync` receives `correlationId` and writes `correlation_id`
>   into the envelope. Callers: the MinIO webhook passes the ID of **that** request (MinIO sends no
>   header, so it is the one the middleware generated), and `VideoReconciliationService` — which
>   has no HTTP request behind it — generates one and **logs what it generated**, otherwise the two
>   sides do not join.
> - `queue.JobState` gained `correlation_id`; `processNextMessage` reads the published state and
>   puts `correlationID` in the job logger, next to `videoID`/`workerID`.
> - `webhook.Notify` sends `X-Correlation-ID`. The API **already** reuses the incoming header
>   (`CorrelationIdMiddleware`), so the callback lands in the log under the same ID — a zero-line
>   change on the receiving side.
>
> **Verified on the running stack**, not just in tests: a job injected into Redis with
> `"correlation_id":"corr-e2e-check"` produced `{"workerID":4,"videoID":"e2e-corr-video",
> "correlationID":"corr-e2e-check","message":"Processing video"}` — the wire format between C# and
> Go really matches.
> Tests: `MinioUploadCompletedTests` (the request ID reaches the envelope, and without an incoming
> header one still goes), `queue_test.go` (the ID **survives every status write** — it is the final
> state the webhook reads) and `webhook_test.go` (header present, and absent when the ID is empty).
> **Verified by mutation:** zeroing the ID in the MinIO endpoint breaks both API tests.
>
> **The runbook changed along:** when the user comes with the ID from the error screen, the Loki
> query now catches API and worker at once.
>
> ~~**Still open:** the envelope's `correlation_id` has no golden in `contracts/` yet — it is
> "discipline", like the rest of the queue contract (see `TODO.md`, contract section).~~
> ✅ *(2026-10-12)* Closed: `contracts/job-state.json` pins `correlation_id` along with the rest of
> the envelope and the queue name, tested from both sides.

### Rung 4 — trace collector → closes F4

Tempo or Jaeger all-in-one in the compose, `OTEL_ENDPOINT: jaeger:4318` in the worker,
OpenTelemetry + instrumentation (ASP.NET Core, HttpClient, Npgsql, StackExchange.Redis) in the API,
and `Serilog.Enrichers.Span` so the `TraceId` lands in the log — that is what makes logs and traces
navigable from each other in Grafana.

It only makes sense **after** rung 3: without propagation, the result is two disconnected traces,
one per service.

---

## What not to do now

- **Alerts before a written SLO.** Without a defined target, an alert becomes noise and drops off
  the radar.
- **Paid APM / Sentry.** The local stack already answers the same questions; the external
  dependency is not the bottleneck today.
- **A new metric** while the six in `metrics.go` have no dashboard anyone looks at.
- **More verbose request/response logging.** `RequestLoggingPipelineBehavior` already serializes
  the whole payload; widening that increases the PII surface in logs. The right path is a redaction
  attribute on the model, not more fields in the log.

---

## Pending decisions

Two things found in passing, still without a decision from the repo owner:

1. ~~**F1 is a doc that lies.**~~ ✅ *(2026-09-09)* — resolved the first way: rung 0 went in and
   the doc became true.
2. **F6.** Move `WriteTo` to the base `appsettings.json` with the `uri` coming from env, or accept
   that Promtail covers the compose case and a deploy outside Docker has no logs.
