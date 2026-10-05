# Fluxo ponta a ponta — do upload ao player

O `README.md` da raiz tem o **mapa** (8 passos + diagrama). Este arquivo tem o que o mapa não
cabe: o formato de cada payload, os retries e timeouts de cada salto, e **o que acontece quando
cada handoff falha**. Cada serviço documenta a própria caixa em `docs/agents/`; aqui só entra o
que atravessa a fronteira.

Tudo foi conferido no código em **2026-10-05** e cita `arquivo:linha` a partir da raiz. O laço do
worker está saindo de `VidroProcessor/main.go` para `VidroProcessor/internal/worker/worker.go`
(P2 do `TODO.md`); para ele as citações usam **nome de função**, que sobrevive à mudança.

Vídeo parado no meio do caminho? O runbook é
[`troubleshooting-stuck-video.md`](troubleshooting-stuck-video.md). Seguir um vídeo pelos logs:
[`observabilidade.md`](observabilidade.md).

---

## Estados do vídeo na API

`VideoStatus` (`VidroApi/src/VidroApi.Domain/Enums/VideoStatus.cs`, valores no golden
[`contracts/enums.json`](../contracts/enums.json)): `PendingUpload` → `Processing` → `Ready` | `Failed`.

| Transição | Quem faz | Onde |
|---|---|---|
| — → `PendingUpload` | criar o vídeo | `CreateVideo.cs:109` (construtor, `Video.cs:31`) |
| `PendingUpload` → `Processing` | webhook do MinIO | `MinioUploadCompleted.cs:86` |
| `PendingUpload` → `Processing` | reconciliação: upload expirou **com** arquivo | `VideoReconciliationService.cs:121` |
| `PendingUpload` → `Failed` | reconciliação: upload expirou **sem** arquivo | `VideoReconciliationService.cs:132` |
| `Processing` → `Ready` | webhook `video-processed` com `success` e `processedPath` | `VideoProcessed.cs:106-107` |
| `Processing` → `Failed` | webhook com `success: false`, ou sem `processedPath` | `VideoProcessed.cs:108-109` |
| `Processing` → `Failed` | reconciliação: `Processing` há mais de 90 min | `VideoReconciliationService.cs:75-87` |

Os `MarkAs*` da entidade não têm guarda (`Video.cs:76-92`): quem impede transição errada são os
handlers, checando o status antes. `Ready` e `Failed` são terminais — nada no código sai deles.

Arquivos da API citados abaixo sem pasta moram em `VidroApi/src/VidroApi.Api/Features/Videos/`,
`VidroApi/src/VidroApi.Api/BackgroundServices/` ou `VidroApi/src/VidroApi.Infrastructure/`.

## Números que governam o fluxo

| O quê | Valor | Onde |
|---|---|---|
| Validade da URL de upload (presigned PUT) | 2 h | `appsettings.json:19` (`MinIO:UploadUrlTtlHours`) |
| Intervalo da reconciliação | 15 min | `appsettings.json:32` |
| `Processing` vira `Failed` após | 90 min sem `UpdatedAt` novo (pior caso do worker, 80 min, + folga) | `appsettings.json:34`, `VideoSettings.cs`; pior caso em [`contracts/processing-timeout.json`](../contracts/processing-timeout.json) |
| TTL do `job:<videoId>` no Redis | 24 h | `RedisJobQueueService.cs:32`, `jobTTL` em `VidroProcessor/queue/job.go` |
| Tentativas por job no worker | 1 + 3 retries (`MaxJobRetries`) | `MaxJobRetries` e `ShouldRetry` em `VidroProcessor/queue/job.go` |
| Orçamento de um job | 18 min (passos 13 min + 5 min de transferência) × `PROCESSING_TIMEOUT_SCALE` | `JobBudget` em `VidroProcessor/internal/processor/processor.go` |
| Lease órfão volta para a fila após | orçamento + 1 min, checado a cada 1 min | `OrphanThreshold` em `internal/worker/worker.go`, `RecoveryInterval` em `queue/client.go` |
| Webhook `video-processed` | até 3 tentativas (só erro de rede, 5xx e 429; outro 4xx falha na hora), timeout 10 s cada, espera 1 s e 4 s | `VidroProcessor/internal/webhook/webhook.go` (`Notify`) |
| Circuit breaker Redis (worker) | abre com 3 falhas seguidas, 30 s aberto | `internal/circuitbreaker/circuitbreaker.go:41-56` |
| Circuit breaker MinIO (worker) | abre com 5 falhas seguidas, 60 s aberto | `circuitbreaker.go:24-39` |
| Tamanho máximo do vídeo | 5120 MB | `VidroProcessor/config/config.go:40`, checado no download (`minio/client.go`, `downloadVideo`) |
| `raw-archived/` apagado após | 30 dias (lifecycle do bucket) | `minio/client.go` (`rawArchivedLifecycleDays`, `configureRawArchivedLifecycle`) |
| Polling do front até status terminal | 3 s | `VidroFront/src/features/videos/hooks.ts:153-164` |

---

## 1. Front → API: criar o vídeo

- **Chamada:** `POST /v1/users/{username}/channels/{handle}/videos`, autenticado
  (`CreateVideo.cs:65,86`). Corpo: `title`, `description`, `tags`, `visibility`. Do front:
  `createVideo` em `VidroFront/src/features/videos/api.ts:82`.
- **Resposta 201:** `videoId`, `uploadUrl` (presigned PUT para `raw/<videoId>`) e
  `uploadExpiresAt` (`CreateVideo.cs:39-44,112-124`).
- **Status:** o vídeo nasce `PendingUpload`, com `UploadExpiresAt = agora + 2 h`.
- **Falha:** síncrona — o front mostra o erro (`UploadVideoForm.tsx:228-231`) e nada foi criado.
  A URL é gerada **antes** do `SaveChanges` (`CreateVideo.cs:114-117`); se o save falhar, sobra só
  uma URL assinada para um id que não existe, sem efeito.

## 2. Browser → MinIO: presigned PUT

- **Chamada:** `PUT <uploadUrl>` direto no MinIO, corpo = o arquivo, `Content-Type` do arquivo ou
  `video/mp4` (`api.ts:95-130`). Bucket `videos` (compose), objeto `raw/<videoId>`.
- **Auth:** a assinatura da URL. Ela assina o header `Host`, por isso a API gera a URL para
  `MinIO:PublicEndpoint` (`localhost:9000`), não para o `minio:9000` interno
  (`MinioSettings.cs:21-27`, `docker-compose.yml:96-99`).
- **Limites:** a URL não restringe tamanho; o limite de 5120 MB só é checado no worker, ao baixar.
- **Falha:** o front mostra o erro e para (`UploadVideoForm.tsx:237-245`) — **sem retry e sem
  resume** (multipart com resume é item de P5 no `TODO.md`). O vídeo fica `PendingUpload` até a
  reconciliação passar depois de `UploadExpiresAt` (2 h + até 15 min) e marcá-lo `Failed`.

## 3. MinIO → API: `POST /webhooks/minio-upload-completed`

- **Configuração:** não está em código — é o `minio-init` do compose que registra o alvo e o
  evento: `notify_webhook:vidroapi` → `http://api:5000/webhooks/minio-upload-completed`,
  `auth_token`, evento `put` com prefixo `raw/` (`docker-compose.yml:73-79`). Fora do compose da
  raiz, ninguém configura isso.
- **Auth:** `Authorization: Bearer <Webhook:MinioUploadToken>`, comparação de string; errado → 401
  (`MinioUploadCompleted.cs:37-41`).
- **Payload lido:** só `EventName` e `Key` do evento do MinIO (`MinioUploadCompleted.cs:17-21`).
  `EventName` diferente de `s3:ObjectCreated:Put` → 200 e ignora (`:43-45`). `Key` vem como
  `videos/raw/<videoId>`; o id é o segmento depois de `raw` (`ParseVideoIdFromKey`, `:61-71`) —
  não parseou → 200 e ignora.
- **Correlation id:** MinIO não manda `X-Correlation-ID`, então o id que o middleware **gera**
  para esta requisição é o que acompanha o job até o worker (`MinioUploadCompleted.cs:51-53`). O
  id da requisição do browser que criou o vídeo **não** é o mesmo; a junção entre os dois é pelo
  `videoId`.
- **Idempotência:** o handler só age em `PendingUpload` (`:82-84`). Evento repetido, vídeo
  inexistente ou já em outro estado → o handler devolve erro de domínio, mas o endpoint ignora o
  resultado e responde **200** (`:55-58`) — o MinIO não reentrega.
- **Falha:**
  - API fora do ar ou 5xx: a entrega fica por conta do MinIO; o compose não configura `queue_dir`
    (`docker-compose.yml:73-75`), então não há fila persistente de eventos do lado do MinIO.
    A rede de segurança é a reconciliação: depois de `UploadExpiresAt`, se `raw/<videoId>` existe,
    ela publica o job e marca `Processing` com um correlation id novo, logado
    (`VideoReconciliationService.cs:104-125`). Custo: até 2 h + 15 min de atraso.
  - Token errado: 401 em todo evento, silencioso para o usuário; mesmo caminho da reconciliação.

## 4. API → Redis: publicar o job

Dentro do mesmo handler (`MinioUploadCompleted.cs`, `Handle`), numa transação, nesta ordem:
`MarkAsProcessing` (em memória) → `SaveChanges` → `PublishJobAsync` → `Commit`
([`design-decisions.md #14`](../VidroApi/docs/agents/design-decisions.md#14-the-job-is-published-inside-the-transaction-that-marks-the-video-processing)).

- **Chaves** (`RedisJobQueueService.cs:19-34`):
  - `SET job:<videoId> <json> EX 86400` — uma **string JSON**, não um hash.
  - `LPUSH video_queue <videoId>` — a fila carrega só o id; tudo o mais está no `job:`.
- **Forma do `job:<videoId>`** (snake_case; o worker desserializa em `JobState`,
  `VidroProcessor/queue/job.go:50-63`):

  | Campo | Escrito por | Conteúdo |
  |---|---|---|
  | `status` | API (`pending`), worker | `pending` \| `processing` \| `done` \| `failed` |
  | `callback_url` | API | `{Api:BaseUrl}/webhooks/video-processed` — `http://api:5000/...` no compose |
  | `correlation_id` | API | id da requisição do passo 3 (ou gerado pela reconciliação) |
  | `retry_count` | API (`0`), worker | incrementado a cada falha |
  | `created_at`, `updated_at` | ambos | Unix seconds; o worker reescreve `updated_at` a cada escrita (`job.go:70`) |
  | `error` | worker | mensagem da última falha |
  | `artifacts`, `metadata` | worker | caminhos no MinIO e saída do `analyze`, no `done` |

- **Nome da fila:** `JobQueueSettings:QueueName` na API (`appsettings.json:69`) e
  `PROCESSING_REQUEST_QUEUE` no worker (`docker-compose.yml:123`) — os dois `video_queue`, mas são
  duas configurações independentes; nenhum teste as liga.
- **Retry:** nenhum. Redis fora → exceção → a transação faz rollback, 500 para o MinIO, o vídeo
  continua `PendingUpload` e cai no caminho da reconciliação do passo 3.
- **`SaveChanges` falha:** falha antes do publish — nenhum job existe. *(Até 2026-10-05 o publish
  vinha antes do save: o job rodava, o webhook do passo 7 era ignorado porque o vídeo não estava
  `Processing`, e a reconciliação marcava `Failed`.)*
- **Falha no meio que sobrou:** o `PublishJobAsync` passa e o `Commit` falha — o caso antigo,
  reduzido ao commit. Mesmo desfecho: processado, webhook ignorado, `Failed` pela reconciliação.

## 5. Redis → worker: consumir com lease

- **Comando:** `BRPOPLPUSH video_queue video_queue:processing 0` (bloqueia sem timeout), dentro do
  circuit breaker do Redis (`VidroProcessor/queue/client.go:47-66`). O id passa atomicamente para
  a lista de lease `video_queue:processing`.
- **Início:** `processNextMessage` lê o `job:` para pegar o `correlation_id` (falta → loga sem o
  campo, não para o job) e grava `status: processing` (`SetJobProcessing`, `job.go:102-113`).
- **Status na API:** nenhum — continua `Processing`. A API não sabe que o worker pegou o job.
- **Worker morre no meio:** o id fica em `:processing`. `recoverStuckJobs` (`queue/client.go:100-148`)
  roda a cada minuto e devolve para `video_queue` todo job em `processing` com `updated_at` mais
  velho que orçamento + 1 min, incrementando `retry_count`; esgotado, manda para
  `video_queue:dead` com `error: "orphaned repeatedly..."`. **Esse caminho não chama o webhook** —
  a API só descobre pela reconciliação de 90 min.
- **`job:` sumiu** (TTL de 24 h, ou `DEL` manual): `SetJobProcessing` devolve `ErrJobStateMissing`
  em vez de recriar o estado, e o worker manda o id para `video_queue:dead` **sem processar**, com
  log `Error` "Job state missing" (`deadLetterJobWithoutState`). O `callback_url` só existe no
  `job:`; processar seria arquivar o raw sem avisar ninguém. `raw/<videoId>` fica intacto para
  reenfileirar à mão (passo 7 do runbook). A API só descobre pela reconciliação de `Processing`.
  *(Até 2026-10-05 o estado era recriado sem `callback_url` e o vídeo era processado em silêncio.)*

## 6. Worker ↔ MinIO: pipeline e artefatos

`processNextMessage` (`internal/worker/worker.go`), em ordem:

1. Baixa `raw/<videoId>` — `StatObject` + checagem de tamanho + `GetObject`
   (`minio/client.go`, `downloadVideo`).
2. Pipeline (`processor.ProcessVideo`): `validate` e `transcode` são
   **críticos** (falha = job falha); `analyze` é semi-crítico (falha = sem metadata); thumbnails,
   áudio, preview e HLS são **não-críticos** (falha = log `Warn` e segue sem o artefato). Cada
   passo tem timeout próprio (constantes `stepTimeout*` em `processor.go`).
3. Sobe `processed/<videoId>_processed` (crítico).
4. Move `raw/<videoId>` → `raw-archived/<videoId>` (copy + remove; falha só loga).
5. Sobe os opcionais; falha de upload só loga.
6. Grava `status: done` com `artifacts` e `metadata`, e dispara o webhook.

**Layout no bucket** (`buildJobArtifacts`):

| Objeto | Escrito por | Observação |
|---|---|---|
| `raw/<videoId>` | browser (passo 2) | some depois do passo 6.4 |
| `raw-archived/<videoId>` | worker | apagado pelo lifecycle em 30 dias |
| `processed/<videoId>_processed` | worker | o MP4 que o player toca; `Content-Type: video/mp4` |
| `thumbnails/<videoId>/thumb_00{1..5}.jpg` | worker | 5 fixos (`thumbnail.go:22`) |
| `audio/<videoId>.mp3` | worker | |
| `preview/<videoId>_preview.mp4` | worker | |
| `hls/<videoId>/…` | worker | `.m3u8` + `.ts`; a API ainda não expõe (P6) |

**Falha e retry** (o `defer` de bookkeeping em `processNextMessage`): qualquer erro crítico →
`SetJobFailed` (`retry_count++`, `error`; nunca recria um `job:<id>` ausente, devolve `ErrJobStateMissing`) → se `retry_count <= 3` (4 tentativas no total), `RequeueJob` (`LPUSH` de novo,
`status: pending`; se o estado sumiu ou não dá para lê-lo, não enfileira e o worker manda o job para o DLQ); senão `LPUSH video_queue:dead` e webhook de falha. Em todos os casos, `LREM`
do lease. Essas escritas usam um contexto que sobrevive ao cancelamento do job e ao SIGTERM, com
teto de 10 s (`bookkeepingTimeout`).

- **Sem distinção entre erro permanente e transitório:** vídeo inválido ou acima de 5120 MB é
  tentado 4 vezes antes do DLQ.
- **Timeout do job** (orçamento estourado): o contexto cancela o FFmpeg em curso, o passo falha e
  entra no mesmo caminho de retry.
- **Depois do arquivamento (6.4) nenhum passo é crítico:** opcionais e o `SetJobDone` só logam a
  falha, então o job não volta para retry com o `raw/` já movido. *(Até 2026-10-05 o publish em
  `video_success_queue` vinha aqui e era crítico: falhar nele mandava o job para retries que
  falhavam todos no download. A fila foi removida — ninguém a consumia.)* Resta um caso estreito:
  Redis fora no fechamento do job deixa `status: processing` e o lease em `:processing`; o
  `recoverStuckJobs` reenfileira e as tentativas falham no download até o DLQ.
- **MinIO fora:** cinco falhas seguidas abrem o breaker por 60 s e os jobs falham rápido, gastando
  retries.

## 7. Worker → API: `POST /webhooks/video-processed`

- **Payload:** os goldens em [`contracts/`](../contracts/README.md) — sucesso completo, sucesso
  mínimo, sucesso sem `processedPath` e falha. Montado por `buildWebhookPayload` a partir do
  `job:`; o tipo é `webhook.Payload` (`webhook.Payload`), camelCase, opcionais
  omitidos. `success` é `status == done`. `thumbnailPaths` são sempre os 5 nomes fixos.
- **Headers:** `Content-Type: application/json`; `X-Webhook-Signature: sha256=<hex>` — HMAC-SHA256
  do corpo cru com `WEBHOOK_SECRET` (`send` em `webhook.go`; o segredo é obrigatório, a assinatura sempre vai); `X-Correlation-ID` com o
  `correlation_id` do job (`send` em `webhook.go`), que a API reaproveita no log dela.
- **Verificação na API:** HMAC do corpo cru com `Webhook:Secret`, comparação em tempo constante
  (`VideoProcessed.cs:46-51,70-78`). Errado ou ausente → 401. No worker o segredo é **obrigatório**
  (`WebhookSecret` em `VidroProcessor/config/config.go`, `notEmpty`): sem ele o worker não sobe —
  até 2026-10-05 era opcional, e vazio fazia toda entrega tomar 401. No compose os dois
  lados usam `dev-webhook-secret` (`docker-compose.yml:130`, `appsettings.Development.json:54`).
- **Status na API:** só age em `Processing` (`VideoProcessed.cs:89-99`). `success && processedPath`
  → `Ready`, grava `VideoArtifacts` e, se os cinco campos de metadata vierem, `VideoMetadata`
  (`:116-147`). Qualquer outra coisa → `Failed`.
- **Idempotência:** como no passo 3, o resultado do handler é ignorado e a resposta é **200**
  (`:57-58`). Webhook repetido, ou que chega depois de a reconciliação já ter marcado `Failed`, é
  descartado — **um vídeo marcado `Failed` por timeout não volta a `Ready`**. Não é mais em
  silêncio: o handler loga `Warning` "Ignoring video-processed webhook" com `VideoId`, status e
  `success`.
- **Retry:** 3 tentativas, 10 s de timeout cada, espera de 1 s e 4 s; qualquer status fora de 2xx
  conta como falha (`webhook.go:42-60,88-90`). Roda numa goroutine solta, com
  `context.Background()`: não segura o job nem o shutdown.
- **Falha:** depois das 3 tentativas (ou na primeira, se for 4xx que não seja 429), só um log `Warn` "Failed to send webhook" (`notifyWebhook`)
  com `videoID` e `callbackURL`. O job já está `done` (ou no DLQ) e não é retentado. A API
  descobre pela reconciliação: 90 min depois do último `UpdatedAt`, `Failed`.
- **Único canal:** o webhook é o único caminho de volta do worker para a API — não existe fila de
  sucesso (a `video_success_queue` foi removida em 2026-10-05; nada a consumia).

## 8. API → player

- O front faz polling de `GET /v1/videos/{videoId}` a cada 3 s até `Ready` ou `Failed`
  (`useVideoStatus`, `hooks.ts:155-164`).
- `GetVideo` devolve URLs **presigned GET**: o MP4 de `ProcessedPath` com validade de 4 h e as
  thumbnails com 1 h (`GetVideo.cs:65-78`, `appsettings.json:20-21`). URL expirada no meio da
  reprodução exige recarregar o vídeo.
- Os caminhos vêm do webhook como estão. Se o upload de um artefato opcional falhou no passo 6.5,
  o caminho foi para o payload mesmo assim (`buildJobArtifacts` olha o resultado do pipeline, não o
  do upload) e a URL gerada aponta para um objeto que não existe.

---

## Quem conserta o quê

| Falha | Rede de segurança | Demora até agir | Resultado |
|---|---|---|---|
| PUT do browser falhou | reconciliação de upload | 2 h + até 15 min | `Failed` |
| Webhook do MinIO perdido | reconciliação de upload (acha `raw/`) | 2 h + até 15 min | job publicado, segue normal |
| Redis fora ao publicar | reconciliação de upload | 2 h + até 15 min | job publicado, segue normal |
| Erro crítico no pipeline | retry do worker | imediato, até 4 tentativas | `Ready`, ou DLQ + webhook → `Failed` |
| Worker morreu com o job | `recoverStuckJobs` | orçamento + 1 min (19 min na escala 1) | retry; esgotado → DLQ **sem** webhook |
| Webhook `video-processed` perdido | reconciliação de processamento | 90 min após o último `UpdatedAt` | `Failed`, mesmo se o worker terminou bem |
| `job:` expirou antes do consumo | DLQ sem processar, log `Error` no worker | imediato no worker; API pela reconciliação de processamento | `Failed`, `raw/` intacto para reenfileirar |

Os 90 min cobrem o pior caso do worker com todos os retries: 4 tentativas × 20 min (a tentativa
mais lenta é a órfã — orçamento de 18 min + 1 min até ser órfã + 1 min até a varredura) = 80 min, na
escala 1. Esse número mora em [`contracts/processing-timeout.json`](../contracts/processing-timeout.json):
o worker testa que a conta dele bate com o golden, a API testa que o timeout dela fica acima.
*(Até 2026-10-05 eram 45 min, calibrados para **uma** tentativa: um job que usava os retries era
marcado `Failed` enquanto o worker ainda tentava.)* O que a folga de 10 min não cobre: espera na fila
— com backlog, um job pode ficar parado em `video_queue` mais que isso antes de começar.

## Como observar

- **Logs:** API (Serilog → Loki) com `CorrelationId`; worker (zerolog JSON) com `videoID`,
  `workerID` e `correlationID` em toda linha do job. As linhas de `recoverStuckJobs` e de
  `notifyWebhook` usam o logger global: têm `videoID`, **não** têm `correlationID`. A chave que
  atravessa tudo — retry, DLQ, reconciliação — é o `videoId`; detalhes em
  [`observabilidade.md`](observabilidade.md).
- **Métricas do worker** (`VidroProcessor/metrics/metrics.go`): `videos_processed_total{status}`,
  `video_processing_duration_seconds`, `video_processing_step_duration_seconds{step}`,
  `active_workers`, `queue_size` (só `video_queue`; `:processing` e `:dead` não têm métrica),
  `video_size_bytes`. Entrega de webhook não tem métrica — só log.
- **Estado cru:** `GET job:<videoId>` e as três listas `video_queue`, `video_queue:processing`,
  `video_queue:dead` — comandos no [runbook](troubleshooting-stuck-video.md).
