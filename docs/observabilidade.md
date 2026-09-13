# Observabilidade — o que existe, o que falta, em que ordem fechar

Levantamento feito em **2026-09-09** sobre os três serviços e o stack do `docker-compose.yml`,
procurando responder uma pergunta só: **dá para reconstruir a vida de um vídeo, ponta a ponta,
sem abrir o código?**

Resposta curta: não. A infra toda já está de pé — Loki, Promtail, Prometheus, Grafana, Serilog,
zerolog, métricas Prometheus no worker e OpenTelemetry instalado. O que falta é o fio condutor:
nenhum identificador atravessa API → Redis → worker → webhook de forma consultável.

Os caminhos citados aqui são relativos à **raiz do monorepo**, não a esta pasta.

---

## Conceitos, o mínimo para ler o resto

**Os três sinais** respondem perguntas diferentes, e a diferença que importa é a cardinalidade:

| Sinal | Responde | Cardinalidade | Onde está aqui |
|---|---|---|---|
| Métrica | quantos, quão rápido, está piorando | baixa — label é conjunto fechado | `VidroProcessor/metrics/metrics.go` → Prometheus |
| Log | o que aconteceu neste caso específico | alta — campo aceita qualquer valor | Serilog / zerolog → Loki |
| Trace | onde o tempo foi gasto, atravessando serviços | alta | `VidroProcessor/internal/telemetry/` → **nada** |

**`videoId` nunca vira label de métrica.** Cada vídeo criaria uma série temporal nova. ID vai em
log e em trace; métrica só recebe dimensão fechada (`status`, `step`) — que é exatamente o que
`metrics.go` já faz.

**Três tipos de identificador, com tempos de vida diferentes:**

- **Correlation ID** — nasce na borda (ou vem do cliente), vive numa requisição HTTP.
  Aqui: `X-Correlation-ID`, em `VidroApi/src/VidroApi.Api/Middleware/CorrelationIdMiddleware.cs`.
- **Trace ID / Span ID** — padrão W3C (`traceparent`), propagado entre processos; o trace é a
  árvore de spans, cada um com pai e duração.
- **ID de negócio** — `videoId`. Não é telemetria, mas neste projeto é o único que sobrevive a
  retry, dead-letter e reconciliação. Job re-enfileirado 40 min depois é outro trace, mesmo
  `videoId` — então **`videoId` é a chave de correlação primária do domínio**, e o trace é o
  detalhe de uma tentativa.

**Propagação de contexto** é o único conceito que custa trabalho: o ID tem de entrar no *envelope*
de cada salto. HTTP tem header; **fila não tem** — quem publica coloca no payload, quem consome
extrai. É onde a cadeia daqui quebra (F3).

---

## O que já existe

### Infra (`docker-compose.yml`)

Loki (`:3100`), Promtail lendo o socket do Docker (todos os containers), Prometheus (`:9090`),
Grafana (`:3001`) com datasources Loki + Prometheus provisionados
(`VidroProcessor/grafana/provisioning/datasources/`) e um dashboard do worker
(`.../dashboards/video-processor.json`).

### `VidroApi`

- `Middleware/CorrelationIdMiddleware.cs` — aceita `X-Correlation-ID` de entrada ou gera um,
  devolve no response e empurra para o `LogContext` do Serilog.
- `Common/Logging/ProcessingLogScope.cs` + `LoggingDefaults.cs` — propriedades `CorrelationId`,
  `ProcessType`, `MethodName` em escopo.
- `Application/Behaviors/RequestLoggingPipelineBehavior.cs` — loga início, sucesso, erro de
  domínio e exceção de cada feature, com request e response serializados.
- `Application/Common/Logging/JsonLogSerializer.cs` — redação por atributo (`[LogIgnore]`,
  `[LogMask]`). **A proteção de PII/segredo em log já está resolvida** e qualquer campo sensível
  novo só precisa do atributo.
- Sink Loki configurado — só em `appsettings.Development.json` (ver F6).

### `VidroProcessor`

- Métricas Prometheus reais em `metrics/metrics.go`: `videos_processed_total{status}`,
  `video_processing_duration_seconds`, `video_processing_step_duration_seconds{step}`,
  `active_workers`, `queue_size`, `video_size_bytes`. Expostas em `main.go:171` (`/metrics`),
  raspadas de 15 em 15s.
- Tracing OpenTelemetry completo em `internal/telemetry/telemetry.go` — exporter OTLP/HTTP,
  no-op se `OTEL_ENDPOINT` vier vazio. Span raiz `process_job` com atributo `video.id` em
  `main.go:220`.

### `VidroFront`

Nada. Sem correlation ID, sem sink de erro de browser.

---

## Furos

Numerados por dor, não por esforço.

### ~~F1~~ ✅ — O runbook promete um campo que o código não escreve *(resolvido em 2026-09-09)*

`docs/troubleshooting-stuck-video.md:95` afirma *"Every pipeline log line carries `videoID`"* e o
passo 4 manda `docker compose logs worker | grep <videoId>`.

Mas os passos do pipeline usam o logger global, sem campo nenhum:
`VidroProcessor/internal/processor/processor.go:151` (`Step 1/7: Validating video`), `:159`, `:171`,
`:187`, e os dois orquestradores de passo não-crítico em `:264` e `:290`. O `videoID` só aparece nas
linhas de `main.go` (`:213`, `:216`, `:241`...), que são a moldura, não o pipeline.

**Consequência:** com `WORKER_COUNT > 1`, `Step 3/7: Transcoding video` de dois vídeos concorrentes
é indistinguível. O passo 4 do runbook falha exatamente sob a carga em que se precisa dele.

> **RESOLVIDO** — degrau 0. `processNextMessage` monta o `jobLogger` uma vez
> (`videoID` + `workerID`) e injeta com `jobLogger.WithContext(ctx)`; os dois orquestradores e os
> passos que já recebiam `ctx` (`analysis.go`, `transcode.go`, `streaming.go`) leem de volta com
> `zerolog.Ctx(ctx)`. **Nenhuma assinatura mudou** e nenhuma dependência entrou.
> `zerolog.DefaultContextLogger` aponta para o logger global, então call site alcançado sem
> logger injetado continua logando em vez de emudecer — é o default que o `zerolog.Ctx` usa
> quando não acha nada no contexto.
> Fora de cobertura de propósito: `video_encoder.go` (roda no startup, antes de existir job) e os
> handlers de health/metrics.

### ~~F2~~ ✅ — O worker loga texto humano, não JSON *(resolvido em 2026-09-09)*

`VidroProcessor/main.go:55`: `log.Logger = log.Output(zerolog.ConsoleWriter{Out: os.Stderr})`.
O Promtail entrega no Loki, mas como linha de texto com escape ANSI — sem campos. Mesmo onde o
`videoID` existe, não dá para filtrar por ele no Loki, só por substring.

> **RESOLVIDO** — degrau 0. `logWriter()` devolve `ConsoleWriter` quando o stderr é TTY e
> `os.Stderr` cru (JSON) no resto. **Sem knob novo:** o destino decide. Rodar o worker no
> terminal continua legível; sob `docker compose` a saída vira JSON e a busca do runbook passa a
> ser `| json | videoID="..."` no Loki.

### F3 — O correlation ID morre na fronteira da fila

`VidroApi/src/VidroApi.Infrastructure/Services/RedisJobQueueService.cs` grava `job:<videoId>` com
`status`, `callback_url`, `retry_count`, `created_at`, `updated_at` e faz `LPUSH` do `videoId` cru.
`VidroProcessor/queue/job.go` (`JobState`) tem os mesmos campos e nenhum de telemetria. O payload do
webhook (`contracts/*.json`) também não.

**Consequência:** API e worker nunca compartilham identificador de telemetria. O `videoId` é o único
laço — e ele funciona (é o que o runbook usa), mas não carrega o correlation ID da requisição do
usuário que originou o upload.

### F4 — Os spans vão para o vazio

`OTEL_ENDPOINT` não está definido no `docker-compose.yml` e não há collector no stack (nem Tempo nem
Jaeger). Todo o `internal/telemetry` roda em modo no-op — por projeto, e está documentado assim em
`VidroProcessor/docs/agents/config.md`. A API não tem OpenTelemetry nenhum: o `.csproj` traz só
Serilog + enrichers.

### ~~F5~~ ✅ — O Prometheus só raspa o worker *(resolvido em 2026-09-13)*

`VidroProcessor/prometheus/prometheus.yml` tem um único `scrape_config`, alvo `worker:8080`. A API
não expõe métrica: taxa de erro HTTP por endpoint, latência, pool do Npgsql e estado do rate limit
são invisíveis. O front também não tem métrica, o que é aceitável por enquanto.

> **RESOLVIDO** — degrau 2, abaixo. O front continua sem métrica, de propósito.

### F6 — O sink do Loki só existe em Development

`VidroApi/src/VidroApi.Api/appsettings.json` (base) tem apenas o bloco `Enrich`; `WriteTo` com
Console + GrafanaLoki está só em `appsettings.Development.json`. Em compose o Promtail salva a
situação (lê o stdout do container), então o furo só aparece em deploy fora de Docker — mas aí a API
não manda log a lugar nenhum.

### ~~F7~~ ✅ — O front é cego *(resolvido em 2026-09-09)*

`VidroFront/src/shared/lib/api-client.ts` monta `Content-Type` e `Authorization` e mais nada: não
gera nem propaga `X-Correlation-ID`, apesar de a API **já aceitar o header de entrada**. O fallback
de erro (commit `5537d16`) mostra a mensagem sem nenhum ID que o usuário possa citar no suporte.

> **RESOLVIDO** — degrau 1. `shared/lib/correlation-id.ts` gera um id por requisição; o
> `apiClient` manda em `X-Correlation-ID`, guarda em `ApiClientError.correlationId` e o
> `RouteErrorFallback` mostra ("Reference for support"). **Mudança de um lado só:** a API já
> aceitava o header e a política de CORS usa `AllowAnyHeader()`, então nada precisou mudar lá.
> **Os dois `features/*/server.ts` entraram junto** — são o caminho de SSR, exatamente onde a
> tela de erro aparece; deixar só o `apiClient` teria coberto o browser e não o loader.
> Upload presigned fica de fora de propósito: vai para o MinIO, não para a API.
> `crypto.randomUUID` é `undefined` fora de contexto seguro, então há fallback — um throw ali
> derrubaria toda requisição, não uma linha de log.

### F7.1 — o front ainda não *lê* o id de volta

O `RouteErrorFallback` mostra o id que o **front gerou**, e a API o reusa, então na prática é o
mesmo valor. O caminho não coberto é o response header (`Access-Control-Expose-Headers` não lista
`X-Correlation-ID`): se um dia a API deixar de reusar o id de entrada, o front mostraria um valor
que não existe em log nenhum. Enquanto o middleware reusar, não vale o diff.

---

## Ordem sugerida

Cada degrau é útil sozinho. A ordem é por retorno sobre diff, não por completude.

### ~~Degrau 0~~ ✅ — logger contextual no worker → fechou F1 e F2 *(2026-09-09)*

Em `processNextMessage` (`VidroProcessor/main.go:200`), montar
`jobLogger := log.With().Str("videoID", videoID).Int("workerID", workerID).Logger()`, injetar no
contexto (`jobLogger.WithContext(ctx)`) e trocar `log.Info()` por `zerolog.Ctx(ctx).Info()` nos
passos. `ProcessVideo` já recebe `ctx`, então **nenhuma assinatura muda**. Recurso nativo do
zerolog, sem dependência nova.

Junto, no `main.go:55`: `ConsoleWriter` só quando a saída é TTY (ou atrás de uma env de dev), JSON
no resto. Aí o `grep` do runbook vira query por campo no Loki, e o texto do runbook passa a ser
verdade.

> **FEITO.** Travado por `TestOrchestrators_LogLinesCarryTheJobFields`
> (`VidroProcessor/internal/processor/orchestration_test.go`): os dois orquestradores rodam com um
> logger de buffer no contexto e toda linha emitida tem de trazer o `videoID`.
> **Verificado por mutação:** trocar `zerolog.Ctx(ctx)` por `zerolog.Ctx(context.Background())` no
> orquestrador sequencial zera a saída e quebra o teste. Docs atualizadas junto:
> `troubleshooting-stuck-video.md` (passo 4, com a query LogQL), `conventions.md` (seção Logging,
> com a regra de logar pelo contexto dentro do job) e `VidroProcessor/docs/OBSERVABILITY.md`.

### ~~Degrau 1~~ ✅ — o front gera o ID → fechou F7 *(2026-09-09)*

`crypto.randomUUID()` por requisição em `api-client.ts`, header `X-Correlation-ID`, guardar o último
no `ApiClientError` e exibir na tela de erro. A API já respeita o header — é mudança de um lado só.
Maior retorno por linha de diff do documento inteiro.

> **FEITO.** Travado por dois testes em `VidroFront/src/tests/api-client.test.ts`: o header sai
> **diferente em cada requisição**, e o id do `ApiClientError` é o mesmo que foi enviado.
> **Verificado por mutação:** remover a linha do header do `apiClient` quebra os dois.
> Docs atualizadas junto: `VidroFront/docs/agents/architecture.md` (responsabilidades do
> `apiClient` + seção Correlation ID) e `features-index.md`.

### ~~Degrau 2~~ ✅ — métricas da API → fechou F5 *(2026-09-13)*

O .NET já emite meters nativos (`Microsoft.AspNetCore.Hosting`, `System.Net.Http`, Npgsql). Falta o
exporter (`OpenTelemetry.Exporter.Prometheus.AspNetCore`) e um segundo job em `prometheus.yml`
apontando para `api:5000`. **Não escrever contador à mão antes disso** — o que já vem de graça cobre
taxa, erro e latência.

> **FEITO.** `AddOpenTelemetry().WithMetrics(...)` no `Program.cs` com quatro meters
> (`Microsoft.AspNetCore.Hosting`, `.Server.Kestrel`, `.RateLimiting`, `Npgsql`) +
> `MapPrometheusScrapingEndpoint()`, e o job `vidro-api` apontando para `api:5000`. **Zero contador
> escrito à mão**, como o degrau mandava. O que aparece no `/metrics`:
> `http_server_request_duration_seconds` (histograma, com `http_route` e status),
> `http_server_active_requests`, `db_client_connection_count{state}`, `db_client_connection_max`,
> `db_client_operation_duration_seconds` e os contadores de bytes do Npgsql.
>
> **Duas coisas achadas no caminho:**
> 1. **O nome do pool do Npgsql vira label.** O default é a connection string — a senha o Npgsql
>    remove (conferido na marra), mas host, porta, banco e usuário ficam, num endpoint sem
>    autenticação, e o valor muda a cada ambiente. `AddInfrastructure` agora constrói um
>    `NpgsqlDataSource` com `Name = "vidroapi"`. Registrado como **design-decisions #12** da API.
> 2. **`/metrics` precisava sair do log de request**, como `/health` já saía: com scrape de 15s,
>    cada raspagem seria uma linha.
>
> Testes: `tests/VidroApi.IntegrationTests/Common/MetricsEndpointTests.cs` — o scrape traz o
> histograma de request e o label do pool é `vidroapi`, não a connection string.
> **Verificado por mutação:** trocar o `Name` do data source deixa o segundo teste vermelho.
>
> **Fica aberto:** o rate limiter só emite métrica quando alguma requisição passa pela política
> `auth`, então `aspnetcore_rate_limiting_*` não aparece num scrape de API ociosa — é o teste de
> carga (P2 do `TODO.md`) que vai exercitar isso. E não há dashboard da API no Grafana: o
> provisionado é só o do worker.

### Degrau 3 — `traceparent` no envelope → fecha F3

Um campo de trace no `jobState` (a API grava, o worker extrai com
`propagation.TraceContext().Extract`) e um header no POST do webhook.

**Isto é contrato compartilhado:** `RedisJobQueueService.cs` e `queue/job.go` mudam no mesmo commit,
e o golden de `contracts/` entra junto se o campo tocar o payload do webhook. Ver a seção "Contrato
compartilhado" do `CLAUDE.md` da raiz.

### Degrau 4 — collector de traces → fecha F4

Tempo ou Jaeger all-in-one no compose, `OTEL_ENDPOINT: jaeger:4318` no worker, OpenTelemetry +
instrumentação (ASP.NET Core, HttpClient, Npgsql, StackExchange.Redis) na API, e
`Serilog.Enrichers.Span` para o `TraceId` cair no log — é o que torna log e trace navegáveis um a
partir do outro no Grafana.

Só faz sentido **depois** do degrau 3: sem propagação, o resultado são dois traces desconexos, um
por serviço.

---

## O que não fazer agora

- **Alerta antes de SLO escrito.** Sem alvo definido, alerta vira ruído e some do radar.
- **APM pago / Sentry.** O stack local já responde as mesmas perguntas; a dependência externa não é
  o gargalo hoje.
- **Métrica nova** enquanto as seis de `metrics.go` não tiverem dashboard que alguém olhe.
- **Log de request/response mais verboso.** `RequestLoggingPipelineBehavior` já serializa payload
  inteiro; ampliar isso aumenta a superfície de PII em log. O caminho certo é atributo de redação
  no modelo, não mais campo no log.

---

## Decisões pendentes

Duas coisas achadas de passagem, ainda sem decisão do dono do repo:

1. ~~**F1 é doc que mente.**~~ ✅ *(2026-09-09)* — resolvido pela primeira via: o degrau 0 entrou e a
   doc passou a ser verdade.
2. **F6.** Mover `WriteTo` para o `appsettings.json` base com `uri` vindo de env, ou aceitar que o
   Promtail cobre o caso do compose e o deploy fora de Docker fica sem log.
