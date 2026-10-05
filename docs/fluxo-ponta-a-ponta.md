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
| `Processing` → `Ready` | webhook `video-processed` com `success` e `processedPath` | `VideoProcessed.cs:97-99` |
| `Processing` → `Failed` | webhook com `success: false`, ou sem `processedPath` | `VideoProcessed.cs:100-101` |
| `Processing` → `Failed` | reconciliação: `Processing` há mais de 45 min | `VideoReconciliationService.cs:75-87` |

Os `MarkAs*` da entidade não têm guarda (`Video.cs:76-92`): quem impede transição errada são os
handlers, checando o status antes. `Ready` e `Failed` são terminais — nada no código sai deles.

Arquivos da API citados abaixo sem pasta moram em `VidroApi/src/VidroApi.Api/Features/Videos/`,
`VidroApi/src/VidroApi.Api/BackgroundServices/` ou `VidroApi/src/VidroApi.Infrastructure/`.

## Números que governam o fluxo

| O quê | Valor | Onde |
|---|---|---|
| Validade da URL de upload (presigned PUT) | 2 h | `appsettings.json:19` (`MinIO:UploadUrlTtlHours`) |
| Intervalo da reconciliação | 15 min | `appsettings.json:32` |
| `Processing` vira `Failed` após | 45 min sem `UpdatedAt` novo | `appsettings.json:34`, `VideoSettings.cs:15` |
| TTL do `job:<videoId>` no Redis | 24 h | `RedisJobQueueService.cs:32`, `VidroProcessor/queue/job.go:34` |
| Tentativas por job no worker | 1 + 3 retries (`MaxJobRetries`) | `VidroProcessor/queue/job.go:32`, `ShouldRetry` em `job.go:136` |
| Orçamento de um job | 18 min (passos 13 min + 5 min de transferência) × `PROCESSING_TIMEOUT_SCALE` | `JobBudget` em `VidroProcessor/internal/processor/processor.go:40` |
| Lease órfão volta para a fila após | orçamento + 1 min, checado a cada 1 min | `main.go` (`StartRecovery(ctx, JobTimeout+1min)`), `queue/client.go:85-97` |
| Webhook `video-processed` | 3 tentativas, timeout 10 s cada, espera 1 s e 4 s | `VidroProcessor/internal/webhook/webhook.go:33,49-58` |
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

Dentro do mesmo handler (`MinioUploadCompleted.cs:86-91`), nesta ordem:
`MarkAsProcessing` (em memória) → `PublishJobAsync` → `SaveChanges`.

- **Chaves** (`RedisJobQueueService.cs:19-34`):
  - `SET job:<videoId> <json> EX 86400` — uma **string JSON**, não um hash.
  - `LPUSH video_queue <videoId>` — a fila carrega só o id; tudo o mais está no `job:`.
- **Forma do `job:<videoId>`** (snake_case; o worker desserializa em `JobState`,
  `VidroProcessor/queue/job.go:47-60`):

  | Campo | Escrito por | Conteúdo |
  |---|---|---|
  | `status` | API (`pending`), worker | `pending` \| `processing` \| `done` \| `failed` |
  | `callback_url` | API | `{Api:BaseUrl}/webhooks/video-processed` — `http://api:5000/...` no compose |
  | `correlation_id` | API | id da requisição do passo 3 (ou gerado pela reconciliação) |
  | `retry_count` | API (`0`), worker | incrementado a cada falha |
  | `created_at`, `updated_at` | ambos | Unix seconds; o worker reescreve `updated_at` a cada escrita (`job.go:67`) |
  | `error` | worker | mensagem da última falha |
  | `artifacts`, `metadata` | worker | caminhos no MinIO e saída do `analyze`, no `done` |

- **Nome da fila:** `JobQueueSettings:QueueName` na API (`appsettings.json:69`) e
  `PROCESSING_REQUEST_QUEUE` no worker (`docker-compose.yml:123`) — os dois `video_queue`, mas são
  duas configurações independentes; nenhum teste as liga.
- **Retry:** nenhum. Redis fora → exceção → 500 para o MinIO, `SaveChanges` não roda, o vídeo
  continua `PendingUpload` e cai no caminho da reconciliação do passo 3.
- **Falha no meio:** se o `PublishJobAsync` passa e o `SaveChanges` falha, o job está na fila mas o
  banco diz `PendingUpload`. O worker processa; o webhook do passo 7 é ignorado (vídeo não está
  `Processing`); a reconciliação, 2 h depois, procura `raw/<videoId>` — que o worker já moveu para
  `raw-archived/` — e marca `Failed`.

## 5. Redis → worker: consumir com lease

- **Comando:** `BRPOPLPUSH video_queue video_queue:processing 0` (bloqueia sem timeout), dentro do
  circuit breaker do Redis (`VidroProcessor/queue/client.go:47-66`). O id passa atomicamente para
  a lista de lease `video_queue:processing`.
- **Início:** `processNextMessage` lê o `job:` para pegar o `correlation_id` (falta → loga sem o
  campo, não para o job) e grava `status: processing` (`SetJobProcessing`, `job.go:94-101`).
- **Status na API:** nenhum — continua `Processing`. A API não sabe que o worker pegou o job.
- **Worker morre no meio:** o id fica em `:processing`. `recoverStuckJobs` (`queue/client.go:99-147`)
  roda a cada minuto e devolve para `video_queue` todo job em `processing` com `updated_at` mais
  velho que orçamento + 1 min, incrementando `retry_count`; esgotado, manda para
  `video_queue:dead` com `error: "orphaned repeatedly..."`. **Esse caminho não chama o webhook** —
  a API só descobre pela reconciliação de 45 min.
- **`job:` sumiu** (TTL de 24 h, ou `DEL` manual): `SetJobProcessing` cria um estado novo **sem
  `callback_url`** (`job.go:95-98`). O vídeo é processado e ninguém é avisado.

## 6. Worker ↔ MinIO: pipeline e artefatos

`processNextMessage` (`internal/worker/worker.go`), em ordem:

1. Baixa `raw/<videoId>` — `StatObject` + checagem de tamanho + `GetObject`
   (`minio/client.go`, `downloadVideo`).
2. Pipeline (`processor.ProcessVideo`, `processor.go:138-189`): `validate` e `transcode` são
   **críticos** (falha = job falha); `analyze` é semi-crítico (falha = sem metadata); thumbnails,
   áudio, preview e HLS são **não-críticos** (falha = log `Warn` e segue sem o artefato). Cada
   passo tem timeout próprio (`processor.go:22-29`).
3. Sobe `processed/<videoId>_processed` (crítico).
4. Move `raw/<videoId>` → `raw-archived/<videoId>` (copy + remove; falha só loga).
5. Sobe os opcionais; falha de upload só loga.
6. `LPUSH video_success_queue <videoId>_processed` (crítico — ver abaixo).
7. Grava `status: done` com `artifacts` e `metadata`, e dispara o webhook.

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
`SetJobFailed` (`retry_count++`, `error`) → se `retry_count <= 3`, `RequeueJob` (`LPUSH` de novo,
`status: pending`); senão `LPUSH video_queue:dead` e webhook de falha. Em todos os casos, `LREM`
do lease. Essas escritas usam um contexto que sobrevive ao cancelamento do job e ao SIGTERM, com
teto de 10 s (`bookkeepingTimeout`).

- **Sem distinção entre erro permanente e transitório:** vídeo inválido ou acima de 5120 MB é
  tentado 4 vezes antes do DLQ.
- **Timeout do job** (orçamento estourado): o contexto cancela o FFmpeg em curso, o passo falha e
  entra no mesmo caminho de retry.
- **Falha depois do arquivamento:** se o passo 6.6 falha (Redis fora ou breaker aberto), o job vai
  para retry — mas `raw/<videoId>` já foi movido, o download das próximas tentativas falha, e o job
  termina no DLQ com webhook de falha. O vídeo vira `Failed` com os artefatos processados no bucket.
- **MinIO fora:** cinco falhas seguidas abrem o breaker por 60 s e os jobs falham rápido, gastando
  retries.

## 7. Worker → API: `POST /webhooks/video-processed`

- **Payload:** os goldens em [`contracts/`](../contracts/README.md) — sucesso completo, sucesso
  mínimo, sucesso sem `processedPath` e falha. Montado por `buildWebhookPayload` a partir do
  `job:`; o tipo é `webhook.Payload` (`internal/webhook/webhook.go:18-31`), camelCase, opcionais
  omitidos. `success` é `status == done`. `thumbnailPaths` são sempre os 5 nomes fixos.
- **Headers:** `Content-Type: application/json`; `X-Webhook-Signature: sha256=<hex>` — HMAC-SHA256
  do corpo cru com `WEBHOOK_SECRET` (`webhook.go:76-80`); `X-Correlation-ID` com o
  `correlation_id` do job (`webhook.go:72-74`), que a API reaproveita no log dela.
- **Verificação na API:** HMAC do corpo cru com `Webhook:Secret`, comparação em tempo constante
  (`VideoProcessed.cs:46-51,70-78`). Errado ou ausente → 401. No worker o segredo é **opcional**
  (`VidroProcessor/config/config.go:34`): vazio, ele manda sem assinatura e toda entrega toma 401. No compose os dois
  lados usam `dev-webhook-secret` (`docker-compose.yml:130`, `appsettings.Development.json:54`).
- **Status na API:** só age em `Processing` (`VideoProcessed.cs:89-91`). `success && processedPath`
  → `Ready`, grava `VideoArtifacts` e, se os cinco campos de metadata vierem, `VideoMetadata`
  (`:108-139`). Qualquer outra coisa → `Failed`.
- **Idempotência:** como no passo 3, o resultado do handler é ignorado e a resposta é **200**
  (`:57-58`). Webhook repetido, ou que chega depois de a reconciliação já ter marcado `Failed`, é
  descartado em silêncio — **um vídeo marcado `Failed` por timeout não volta a `Ready`**.
- **Retry:** 3 tentativas, 10 s de timeout cada, espera de 1 s e 4 s; qualquer status fora de 2xx
  conta como falha (`webhook.go:42-60,88-90`). Roda numa goroutine solta, com
  `context.Background()`: não segura o job nem o shutdown.
- **Falha:** depois das 3 tentativas, só um log `Warn` "Failed to send webhook" (`notifyWebhook`)
  com `videoID` e `callbackURL`. O job já está `done` (ou no DLQ) e não é retentado. A API
  descobre pela reconciliação: 45 min depois do último `UpdatedAt`, `Failed`.
- **`video_success_queue`:** recebe `<videoId>_processed` a cada sucesso (`queue/client.go:76-81`),
  mas **nenhum código da API consome essa lista** — não é canal de recuperação hoje.

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
| Webhook `video-processed` perdido | reconciliação de processamento | 45 min após o último `UpdatedAt` | `Failed`, mesmo se o worker terminou bem |
| `job:` expirou antes do consumo | nenhuma no worker | — | processa sem avisar; reconciliação → `Failed` |

O 45 min é calibrado contra **uma** tentativa (18 + 19 min, comentário em `VideoSettings.cs:9-14`).
Um job que falha devagar e usa os retries pode passar disso e ser marcado `Failed` pela API
enquanto o worker ainda tenta; o webhook que chegar depois é descartado.

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
