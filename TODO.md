# TODO — Vidro (VidroApi · VidroFront · VidroProcessor)

Levantamento feito em 2026-08-29. Cada item tem referência de arquivo/linha para
não precisar redescobrir o problema.

**Fechado até agora:** *(2026-08-29)* BUG-1 (webhook de sucesso parcial), BUG-2 (orçamento
do job), CORS na API, porta 5000 unificada, P0.1 inteiro (docs mentirosas), compose único
na raiz + `/health` (os três finalmente rodam juntos, E2E verificado), **P1 inteiro**
(CI nos três repos, rate limiting, migrations no startup). *(2026-08-30)* testes de `queue/`
no Processor, **migração para monorepo** (os três viraram um repo só; ver `docs/MONOREPO.md`) e a
**consolidação das docs** (runbook de vídeo preso subiu para `docs/` da raiz; `roadmap.md` do
Processor virou o P6 daqui; `workflow.md` do Front, o `docker-compose.yml` do Processor e as
regras repetidas nos três `CLAUDE.md` foram removidos; README da raiz criado).
Itens marcados `[x]` trazem o commit e o que ficou no lugar.

**Estado geral:** as features estão prontas (todas as fases do Front ✅, plano da
API 100% marcado, Processor ~95%). O que falta não é feature — é (a) os três
nunca rodarem juntos, (b) documentação que mente, (c) lacunas de teste na lógica
de confiabilidade, (d) acabamento de UX.

---

## P0 — Bugs de contrato entre repos (achados nesta análise, não registrados em lugar nenhum)

### ~~BUG-1~~ ✅ — Webhook `video-processed` quebra quando um artefato opcional falta → vídeo preso em `Processing` para sempre

`VidroApi/src/VidroApi.Api/Features/Videos/VideoProcessed.cs:98-116`
(`PersistSuccessfulProcessing`).

O contrato documentado pelo Processor
([`VidroProcessor/docs/agents/design-decisions.md` #3](VidroProcessor/docs/agents/design-decisions.md#3-critical-vs-non-critical-pipeline-steps))
diz, textualmente:
*"success webhook may contain empty `thumbnailPaths`, `hlsPath`, `previewPath`,
or `audioPath`. API must not treat missing optional artifacts as failure."*
Só thumbnails/audio/preview/streaming são não-críticos — o orquestrador engole o
erro e reporta sucesso sem aquele path
([`conventions.md`](VidroProcessor/docs/agents/conventions.md#critical-vs-non-critical-pipeline-steps)).

A API não cumpre esse contrato. `PersistSuccessfulProcessing` usa `!`
(null-forgiving) em todos os campos opcionais, e a entidade rejeita os nulos:

- `VideoArtifacts` (`Domain/Entities/VideoArtifacts.cs:19-22`) faz
  `ArgumentException.ThrowIfNullOrWhiteSpace` em `previewPath` e `audioPath`, e
  `ArgumentNullException.ThrowIfNull` em `thumbnailPaths`. **Só `hlsPath` é
  nullable.**
- O EF confirma: `VideoArtifactsConfiguration.cs` marca `preview_path` e
  `audio_path` como `IsRequired()`; só `hls_path` é `IsRequired(false)`.
- Pior no metadata: `cmd.FileSizeBytes!.Value`, `DurationSeconds!.Value`,
  `Width!.Value`, `Height!.Value`, `Codec!` estouram `InvalidOperationException`
  se o step `analyze` falhar — e `analyze` é semi-crítico
  (`VidroProcessor/docs/agents/architecture.md:93`: *"errors logged, downstream
  metadata missing in webhook"*).

**Cadeia de falha:** step não-crítico falha (ex.: glitch na thumbnail — o exemplo
que o próprio `design-decisions.md` usa para justificar o design) → Processor
reporta **sucesso** → API estoura no handler → 500 → o Processor tenta 3x com
backoff e desiste (`internal/webhook/webhook.go:46-56`) → o job já foi ack'ado,
então **não vai para o DLQ** → o vídeo fica em `Processing` **permanentemente**.
E `VideoReconciliationService.cs:41` só resgata vídeos em `PendingUpload` com
`UploadExpiresAt` vencido — **não olha para `Processing`**. Ninguém percebe.

- [x] Tornar `PreviewPath` e `AudioPath` nullable em `VideoArtifacts` + config EF +
      migration (`HlsPath` já era). `ThumbnailPaths` vira lista vazia em vez de null —
      os 5 call sites que fazem `AddRange` continuam intactos.
- [x] Tornar os campos de `VideoMetadata` tolerantes a `analyze` falho.
- [x] Remover os `!` de `PersistSuccessfulProcessing` e tratar ausência de fato.
- [x] Fazer o `VideoReconciliationService` também resgatar vídeos travados em
      `Processing` além do tempo esperado.
- [x] Teste de integração do webhook com payload de sucesso parcial
      (sem thumbnails, sem audio, sem preview, sem metadata).

> **RESOLVIDO** — `VidroApi`. `VideoArtifacts` agora exige só `ProcessedPath`;
> `PersistSuccessfulProcessing` só grava `VideoMetadata` quando o bloco do `analyze` veio
> inteiro (`Video.Metadata` já era nav opcional e todo consumo já usava `?.`).
> Sucesso **sem** `processedPath` vira `Failed` em vez de estourar — não sobra caminho de
> exceção no handler. Migration `MakeOptionalArtifactPathsNullableMigration`.
> `DeleteVideo`/`DeleteChannel` ganharam guard de null antes de enfileirar o cleanup.
> Rede de segurança: `VideoReconciliationService.ReconcileStuckProcessingAsync` marca como
> `Failed` vídeo em `Processing` há mais de `VideoSettings:ProcessingTimeoutMinutes`
> (default **45min**, acima dos 18min de orçamento + 19min de requeue de órfão do Processor).
> Testes: `VideoProcessedTests` — sucesso sem artefato opcional nenhum → `Ready` com
> `thumbnailUrls` vazio; sucesso sem `processedPath` → `Failed`. 324 testes passando.

> Nota: o antigo `VidroProcessor/docs/roadmap.md` (P-OPT1, seção "Coordination" — hoje P6
> deste arquivo) já tinha
> sinalizado exatamente esse risco — *"delete/cleanup + `VideoProcessed` handler
> must tolerate missing optional artifacts"* — e ninguém agiu.

### ~~BUG-2~~ ✅ — Timeout global do job (5 min) era menor que o próprio pipeline (13 min)

`VidroProcessor/main.go:196` cria `processCtx` com **5 minutos**, e esse orçamento
cobre download do MinIO + os 7 steps + upload de todos os artefatos.

A soma dos timeouts por step (`internal/processor/processor.go:23-29`) é de
**13 minutos**: validate 30s + analyze 30s + transcode 3min + thumbnails 60s +
audio 2min + preview 2min + streaming 4min.

O próprio `docs/agents/design-decisions.md` define a regra: *"whole-job budget
must always exceed sum of critical-path steps (validate + analyze + transcode)
plus download + upload slack"*. Só o caminho crítico já são 4min, sobrando ~1min
para baixar e subir tudo — e o P6 aqui embaixo diz que um vídeo de 500 MB / 1080p leva
"vários minutos" hoje (a meta de 2–3 min é *pós*-otimização).

**Consequência:** vídeo grande estoura o `processCtx` no meio do pipeline → job
falha → 3 retries → DLQ. Um vídeo perfeitamente bom nunca processa.

- [x] Subir o orçamento global, ou derivá-lo da soma dos steps.
- [x] Expor os timeouts como env vars.

> **RESOLVIDO** — `VidroProcessor` `4f4a949`. `processor.JobBudget(scale)` deriva o
> orçamento da soma dos 7 steps + 5min de folga de transferência (18min no scale 1);
> `main.go` usa `jobTimeout(cfg)`. Knobs: `PROCESSING_TIMEOUT_SCALE` (multiplica todos
> os step timeouts e o global) e `JOB_TIMEOUT` (sobrescreve o global).
> Achado no caminho: `StartRecovery` requeueava job em `:processing` há >10min — com
> orçamento de 18min isso reprocessaria job **saudável**; agora o limiar é
> `jobTimeout + 1min`. Invariante travada em `internal/processor/timeout_test.go`.

---

## ~~P0.1~~ ✅ — Documentação que mente (barato, destrava o resto)

### Documentação que envenena o trabalho com IA

- [x] **`VidroApi/docs/agents/features-index.md`: 4 rotas erradas.** Corrigidas para os
      paths reais (`/v1/users/{username}/channels/{handle}/...` e `/v1/users/{username}/playlists`).
      Conferi o índice inteiro contra os 43 `Map*` do código: as outras 39 batem.
- [x] **Ponteiros de caminho mortos no Front.** `../Api/` → `../VidroApi/` (3 ocorrências
      em `VidroFront/CLAUDE.md`, 1 em `workflow.md`).
- [x] **`VidroFront/docs/agents/workflow.md:7`** — o caminho proibido virou "fora deste
      repo (`VidroFront/`)", sem path absoluto para apodrecer de novo.
- [x] **`VidroProcessor/docs/documentation.md` deletado.** 187 linhas que duplicavam o
      README e o `docs/agents/architecture.md` e mentiam em 6 pontos. As 3 referências no
      README apontam agora para `architecture.md` + `GETTING_STARTED.md`.
      Verifiquei no código antes de apagar: `BRPopLPush` (`queue/client.go:47`),
      `MINIO_USE_SSL` (`config/config.go:24`), `.env` ausente já tolerado
      (`config.go:60`, `errors.Is(err, os.ErrNotExist)`).
- [x] **`VidroProcessor/README.md`**: badge `bugs known` → `functional` apontando para o
      roadmap (que hoje é o P6 deste arquivo); `Version: 0.1.0` removido (não existe tag nenhuma nos três repos, era
      ficção) e data atualizada.

### Afirmações falsas nos docs

- [x] `VidroApi/CLAUDE.md:77` — a afirmação de deploy automático virou o estado real:
      só `ci.yml`, só build+test, só em PR para `master`, deploy manual. A linha de
      "produção sai de tags" ficou marcada como estratégia pretendida nos dois CLAUDE.md
      (VidroApi e VidroFront) — nenhum repo tem tag.
- [x] `VidroFront/docs/agents/workflow.md` — "Fase 6 ✅ SSR + **SEO**" → só SSR, com a
      nota de que só `__root.tsx` define `head`. De quebra: fase 9 (Playlists) estava
      sem ✅ e está pronta (feature + rota + plano com 0 pendências).
- [x] `VidroProcessor/docs/roadmap.md` (P-OPT1, hoje P6 daqui) — **a afirmação do roadmap estava certa e
      este TODO é que estava errado.** O fluxo de watch serve o MP4 mesmo: `GetVideo`
      monta `videoUrl` a partir de `ProcessedPath` e nunca expõe `hlsPath`. O que o
      `VideoPlayer.tsx:16` prova é que o *player* já toca `.m3u8` via `hls.js` — falta só
      o campo da API. Reescrevi com essa precisão e um aviso para não deletar o step de HLS.

## ~~P1~~ ✅ — Fazer o stack rodar junto

- [x] ~~**A API não tem CORS.**~~ **RESOLVIDO** — `VidroApi` `4b29dc6`. Política default
      lendo `Cors:AllowedOrigins` da config (`http://localhost:3000` em Development),
      `app.UseCors()` antes do `UseAuthentication` para o 401 também sair com os headers.
      Sem `AllowCredentials` (Bearer em header, refresh token é cookie do server TanStack).
      Fora de dev fecha por padrão + `Log.Warning` no startup se a lista vier vazia.
      Testes: `tests/VidroApi.IntegrationTests/Common/CorsTests.cs`.
- [x] **Porta da API unificada em 5000** — `VidroApi` `85520e3` + `VidroProcessor` `dde09fe`.
      O front sempre apontou para `:5000`, mas o `launchSettings.json` subia em `:5144`,
      e o `docker-compose.yml` do Processor mandava o webhook `minio-upload-completed`
      para `:5144` — upload nunca notificaria a API.
- [x] **Dockerfile da API** (`VidroApi/Dockerfile` + `.dockerignore`). Multi-stage
      sdk:10.0 → aspnet:10.0. A imagem runtime instala `libgssapi-krb5-2` (sem ela o
      Npgsql loga erro de GSSAPI em toda conexão) e `curl` (healthcheck do compose).
- [x] **`docker-compose.yml` único na raiz `vidro/`** — postgres, redis, minio,
      minio-init, api, worker, front, loki, promtail, prometheus, grafana.
      Grafana foi para **3001**: 3000 é do front. `minio-init` agora aponta o webhook
      para `http://api:5000` e espera a API ficar *healthy* (o MinIO valida a config
      discando o endpoint — antes falhava com `no such host`).
      O compose de cada repo continua existindo para trabalho isolado.
- [x] **`/health` na API** — `AddHealthChecks()` + `MapHealthChecks("/health")`, só
      liveness (sem probe de dependência: Redis instável não pode derrubar o container).
- [x] **Migrations no startup em Development** — `docker compose up` tem que entregar
      uma API que funciona, e o Postgres do compose sobe vazio. Fora de Development
      continua sendo passo manual de deploy.

> **Dois bugs achados só porque a stack rodou de verdade:**
> 1. **URL presignada inalcançável pelo browser.** A API assinava com `minio:9000`
>    (endpoint interno) e o browser não resolve esse host — upload e player quebrados.
>    Trocar a string do host não resolve: a assinatura SigV4 cobre o header `Host`.
>    Correção: `MinIO:PublicEndpoint` + um `IMinioClient` dedicado (keyed
>    `minio-presign`) que assina para o host público. Só os dois métodos de presign
>    usam esse client; o resto continua no endpoint interno.
> 2. **Front SSR e browser precisam de URLs diferentes para a API.** `api-client.ts`
>    usava só `import.meta.env.VITE_API_URL`, que o Vite congela em build (valor do
>    browser). No container o SSR precisa de `http://api:5000`. Passou a preferir
>    `process.env.VITE_API_URL` quando roda no servidor — mesmo padrão que os
>    `features/*/server.ts` já usavam.
>
> Ambos travados por teste: `VidroApi` `tests/.../Common/MinioPresignEndpointTests.cs`
> (o client de presign resolve para o endpoint público, e cai no interno quando não há um)
> e `VidroFront` `src/tests/api-base-url.test.ts` (no servidor o runtime vence o valor de build).
>
> **E2E verificado de ponta a ponta** nesta stack: signup → canal → vídeo → PUT
> presignado → webhook do MinIO → fila Redis → worker (7 steps) → webhook
> `video-processed` → `Ready`, com `videoUrl` e 5 thumbnails devolvendo **200** no
> browser. Era o fluxo que nunca tinha rodado inteiro.
- [x] **CI nos três repos.** `.github/workflows/ci.yml` no Front (bun install → test →
      build) e no Processor (setup-go 1.25 → ffmpeg → build → vet → `go test ./... -cover
      -timeout 15m`). Os três agora disparam em **push para `master` além de PR** — o
      gatilho só-PR do VidroApi quase nunca rodava, já que a convenção do repo é commitar
      direto na master. Rodei os dois pipelines localmente antes de commitar.
      **Sem step de lint no Front:** `biome check` acusa 108 erros no código existente, então
      ligar isso deixaria o CI vermelho no primeiro push. Limpar o código antes — ver
      "Limpar o que o `biome check` acusa" em Sujeira pequena.
      *(2026-08-30: com o monorepo, os três `ci.yml` viraram
      `.github/workflows/{api,front,processor}.yml` na raiz, cada um com filtro `paths:`.)*
- [x] **Rate limiting em `SignIn`/`SignUp`** — `AddRateLimiter` nativo do .NET 10, zero
      dependência nova. Fixed window por IP, política `auth`, 429 na rejeição, configurável
      em `RateLimit:AuthPermitLimit` / `AuthWindowSeconds` (10/min por padrão), seguindo o
      padrão de Options validadas do repo. Só os dois endpoints de credencial são limitados —
      limitador global estrangularia navegação normal.
      Testes: `tests/VidroApi.IntegrationTests/Auth/RateLimitTests.cs` (factory própria com
      budget de 3). Verificado também na stack real: 10× 401 e depois 429.
- [x] **Migrations no startup em todo ambiente** (antes era só `Development`). Deploy da
      imagem passa a ser o deploy inteiro. **Consequência a lembrar:** o schema muda antes da
      instância antiga parar, então migration tem que ser retrocompatível com a versão ainda
      no ar; duas instâncias subindo juntas ambas tentam migrar (o EF Core 10 tem
      `IMigrationsDatabaseLock`, então a perdedora espera em vez de corromper).
      Docs do README/compose corrigidas junto.
- [ ] **Quando existir pipeline de deploy, mover a migration para um release step.**
      Nenhuma plataforma garante "instância antiga parada antes da nova subir" por padrão —
      rolling deploy sobrepõe as duas de propósito, é assim que zero-downtime funciona.
      Dá para forçar (`strategy: Recreate` no k8s, `minimumHealthyPercent: 0` no ECS), mas o
      preço é downtime. O que resolve de verdade é um **release step** que roda a migration
      uma vez, antes de qualquer instância nova pegar tráfego: `release` phase (Heroku),
      `[deploy] release_command` (Fly.io), Pre-Deploy Command (Render/Railway), Job de
      `pre-upgrade` do Helm ou PreSync do Argo (k8s), ou um step no próprio CI (ECS, Cloud
      Run, App Service — esses não têm hook nativo). `dotnet ef migrations bundle` gera um
      executável self-contained feito para isso.
      **Mesmo com release step**, rolling deploy deixa o código antigo rodando contra o
      schema novo por uma janela → migration retrocompatível (expand/contract) continua
      obrigatória. Só dá para escapar disso aceitando downtime.
      Enquanto não há pipeline (deploy é manual hoje), `Migrate()` no startup está de bom
      tamanho.

---

## P2 — Testes

### VidroProcessor — a resiliência não é testada (lacuna mais cara dos três repos)

Os 7 steps do pipeline têm todos `_test.go`, o que faz parecer bem coberto. Mas:

- [ ] **`internal/processor/processor.go` (253 linhas) — zero testes.**
      É o orquestrador: classificação de step crítico vs não-crítico, timeouts por
      step, política de cancelamento.
- [x] ~~**`queue/` (283 linhas, `client.go` + `job.go`) — zero testes unitários.**~~
      **RESOLVIDO** — `VidroProcessor` `8d1b98d`. 12 testes em `queue/queue_test.go`,
      cobertura 0% → **60.3%**, rodando em 8ms.
- [ ] Os testes de integração de fila (`test/integration/queue_test.go`) cobrem só
      caminho feliz: `PublishAndConsume`, `MultipleMessages`, `EmptyQueue`,
      `SuccessQueue`. Nenhum de falha — e **nenhum deles importa o pacote `queue`**:
      falam com o `go-redis` direto, ou seja, testam a biblioteca, não o nosso código.
      Com os unitários de `queue/` no lugar, o valor de reescrevê-los caiu bastante;
      o que sobraria de útil é um teste de falha ponta a ponta (worker morre no meio →
      job sobrevive no `:processing` → recovery pega).

> **RESOLVIDO (queue/)** — harness: `miniredis` (dep nova, só de teste, pure Go). O motivo
> de não usar o testcontainers que já estava instalado é o próprio item abaixo: teste atrás
> de Docker não roda. Os testes ficam *dentro* do pacote, então apontam os globais
> `client`/`cfg` para o fake e chamam `recoverStuckJobs` direto, sem esperar o ticker de 1min
> e sem refatorar produção para injetar interface.
>
> **Dois achados no caminho, ambos corrigidos no mesmo commit:**
> 1. A decisão retry-vs-DLQ não estava em `queue/` — estava inline no `main.go:225`
>    (`state.RetryCount <= queue.MaxJobRetries`). Virou `JobState.ShouldRetry()`. Teste que
>    *copia* a condição não pega drift; era preciso trazer o invariante para dentro do pacote.
> 2. **Bug: `recoverStuckJobs` reenfileirava órfão para sempre.** Incrementava `RetryCount`
>    e dava requeue sem nunca olhar `MaxJobRetries` — um vídeo que trava o worker orfana toda
>    vez e nunca chegava ao DLQ, ao contrário do caminho de falha do worker, que respeita o
>    limite. Agora esgotado vai para o DLQ (`client.go:109`).
>
> Os dois testes que travam isso foram **verificados por mutação**: trocar `<=` por `<` em
> `ShouldRetry` quebra `TestShouldRetry_Boundary`; remover o guard do recovery quebra
> `TestRecoverStuckJobs_ExhaustedOrphanGoesToDLQ`.
>
> Fora de cobertura de propósito (os 39.7% restantes): `InitRedisClient` (`log.Fatal`),
> o loop do ticker em `StartRecovery` (a lógica está em `recoverStuckJobs`, essa testada),
> `HealthCheck`, `GetQueueSize`, `SetJobProcessing`/`SetJobDone` (setters triviais).
- [ ] Agravante: os testes pulam sozinhos sem Docker/ffmpeg — na prática raramente rodam
      fora do CI (que já existe desde 2026-08-29 e tem Docker + ffmpeg). Os 12 de `queue/`
      são a exceção: `miniredis` é in-process, rodam sempre.
- [x] ~~**`VidroProcessor/docs/TESTING.md` — lista "What's Missing" desatualizada.**~~
      **RESOLVIDO** — `VidroProcessor` `8d1b98d`. Tabela de cobertura com `queue` 60.3%,
      `queue` fora do "No tests", seção nova com os 12 testes, e o "What's Missing"
      reescrito: saíram `queue.ConsumeMessage`/`PublishSuccessMessage`, entraram
      `processor.go` e o que sobrou sem cobertura no `queue/`. Continuam válidos:
      `config.LoadConfig()`, `minio.DownloadVideo()`/`UploadVideo()`,
      `main.processNextMessage()`, benchmarks de transcode.

### VidroFront — cobertura de fachada

- [ ] 23 testes em 4 arquivos, **todos da camada de API com fetch mockado**.
      Para 35 componentes e 11 rotas há **0 testes de renderização**.
      `@testing-library/react`, `@testing-library/dom` e `jsdom` estão instalados
      e nunca usados.
- [ ] Features **`channels`, `comments` e `playlists` não têm nenhum teste** —
      e `CommentList.tsx` é o componente mais complexo do app (8 estados de pending,
      replies aninhadas, edição inline, reações).

### VidroApi — bom, só limpar

- [ ] Excelente no geral: 287 `[Fact]`/`[Theory]`, 43 arquivos de integração para
      43 features (≈1:1 por endpoint, com testcontainers). Nada a fazer além de:
- [ ] Deletar `tests/VidroApi.UnitTests/UnitTest1.cs` — template vazio do
      `dotnet new`, um `[Fact]` de corpo vazio que sempre passa.

### Nos três

- [ ] Nenhum teste E2E do fluxo que define o produto: upload → processamento → play.
      Cada repo testa a própria borda; a integração entre eles não é testada.

### Contrato entre serviços — destravado pelo monorepo (2026-08-30)

Os dois P0 desta lista foram divergência de contrato entre serviços. Enquanto eram três repos,
não havia onde colocar a rede que os pegaria. Agora há. Ver `docs/MONOREPO.md`, seção "O que a
migração destrava".

- [ ] **Gerar os tipos do front a partir do OpenAPI da API.**
      `VidroFront/src/shared/types.ts:64-69` espelha **na mão** seis enums do backend
      (`VideoStatus`, `VideoVisibility`, `ReactionType`, `PlaylistVisibility`, `PlaylistScope`,
      `CommentSortOrder`), e as shapes de request/response de cada feature são redigitadas em
      `features/*/types.ts`.
      **Conferi os seis: batem com o backend hoje** — cinco contra `VidroApi/src/VidroApi.Domain/Enums/`
      e `CommentSortOrder` contra `Features/Comments/ListComments.cs:19`. O problema não é estarem
      errados, é **nada garantir que continuem certos**: `ReactionType` começa em `1`, não em `0`,
      e é exatamente o tipo de detalhe que um refactor no backend leva junto sem ninguém notar no
      front. O P0.1 ("4 rotas erradas no `features-index.md`") foi essa mesma classe de drift, só
      que em doc.
      Caminho: `MapOpenApi()` já existe (`VidroApi/src/VidroApi.Api/Program.cs:76`) mas está sob
      `if (app.Environment.IsDevelopment())` — para gerar em CI, ou sobe a API em Development e
      busca `/openapi/v1.json`, ou adiciona `Microsoft.Extensions.ApiDescription.Server`, que
      emite o JSON no build sem subir nada. Daí `openapi-typescript` gera o `.d.ts`, e um job de
      CI regenera e falha se o diff não for vazio.
      **Não é drop-in:** a API envelopa tudo em `{ data: T }` e devolve enum como
      `EnumValue { id, value }` — o tipo gerado descreve o envelope, e o `apiClient` é quem
      desembrulha. Planejar a camada fina antes de trocar os tipos escritos à mão.

- [ ] **Fixture de contrato compartilhada entre Processor e API para o webhook.**
      `VideoProcessedTests` (feito no BUG-1) já cobre payload de sucesso parcial — mas o JSON do
      teste foi **escrito à mão do lado da API**, e nada o amarra ao que o worker realmente
      emite (`VidroProcessor/internal/webhook/webhook.go:17-30`, struct `Payload`, camelCase,
      `omitempty` em tudo menos `videoId`/`success`).
      Versão preguiçosa que já resolve: um JSON golden versionado, o worker testa que **serializa
      exatamente aquilo** e a API testa que **aceita exatamente aquilo**. Divergência quebra um dos
      dois lados no mesmo CI. Antes do monorepo isso exigia publicar um pacote; agora é um arquivo.
      Cobrir os três casos que o BUG-1 provou serem reais: sucesso completo, sucesso sem nenhum
      artefato opcional e sem bloco de metadata, e sucesso sem `processedPath` (→ `Failed`).
      O mesmo vale para o resto do contrato, hoje só documentado: nome da fila
      (`JobQueueSettings:QueueName` ↔ `PROCESSING_REQUEST_QUEUE`), `callback_url` no `JobState`,
      e o layout de paths no MinIO.

---

## P3 — UI / UX do frontend

**O que já está bom (não mexer):** estados consistentes — skeletons em 7 telas,
empty states em 12 lugares com texto próprio, `isPending` tratado em 23 arquivos,
toasts (`sonner richColors`), forms com react-hook-form + zod, shadcn/ui coerente.

### Features construídas e desligadas

- [ ] **Busca é uma feature morta de ponta a ponta.** A API tem `SearchVideos`
      (`GET /v1/videos/search`) implementado e testado; o front não tem função de
      API para ela; `src/routes/search.tsx` inteiro retorna `<p>Search</p>`; e o
      `Header.tsx` não tem campo de busca nem link para `/search`. Numa plataforma
      de vídeo é a lacuna nº1 de UX — e o backend já está pronto.
- [ ] **`src/components/ThemeToggle.tsx` é código morto** — nunca importado em lugar
      nenhum. `src/styles.css:29` já tem a variante `.dark` inteira estilizada.
      Dark mode está construído e inalcançável. Plugar no Header.

### Acabamento

- [ ] **Header não é responsivo** — 4 botões com rótulo de texto num flex row, zero
      breakpoints no arquivo. Estoura no mobile. (O app todo tem ~20 usos de
      breakpoint, quase todos `grid-cols`.)
- [ ] **`<html lang="pt-BR">` com a UI toda em inglês** (`src/routes/__root.tsx:76`),
      datas com `toLocaleDateString('en-US')` (`watch.$videoId.tsx:38`), e o Header
      mistura "Upload"/"Dashboard"/"Sign out" com **"Meu Perfil"**. Escolher um
      idioma — `lang` errado também é bug de leitor de tela.
- [ ] **SEO é zero.** Só `__root.tsx` define `head`, então toda página é
      `<title>Vidro</title>`, sem `description`, sem OG, sem Twitter card.
      Compartilhar link de vídeo não gera preview. Com SSR já funcionando, é jogar
      fora o motivo de ter SSR.
- [ ] **Nenhum `errorComponent` ou `notFoundComponent` em nenhuma rota.** URL
      inválida ou erro de loader = tela quebrada sem caminho de volta.
- [ ] `index.tsx` (home) e `dashboard.tsx` tratam `isPending` mas não `isError`.
- [ ] **Devtools vão para produção** — `__root.tsx:80` renderiza `<TanStackDevtools>`
      sem guard de `import.meta.env.DEV`.
- [ ] **A11y rasa** — só 10 arquivos têm qualquer `aria-*`/`alt`/`sr-only`, e o
      Header não tem nenhum.

---

## P4 — Documentação para humanos

- [x] **README na raiz** *(2026-08-30)* — o que é o Vidro, os três serviços, como subir o stack,
      portas/credenciais, testes, convenções e índice das docs da raiz.
- [ ] **Não existe doc do fluxo ponta a ponta.** Cada serviço documenta a própria caixa;
      o caminho que importa — upload → presigned PUT → webhook `minio-upload-completed`
      → fila Redis → pipeline → webhook `video-processed` → HLS no player — atravessa
      os três. É a coisa mais difícil de reconstruir sozinho.
      *(2026-08-30: o `README.md` da raiz agora traz esse fluxo em 8 passos + diagrama. Serve de
      mapa, não substitui a doc — falta shape de cada payload, retries, e o que acontece quando
      cada handoff falha.)*
- [ ] **`VidroApi/README.md` tem 46 linhas e o quickstart não funciona.**
- [ ] **60% do markdown do projeto é arqueologia.** 4.797 de 7.992 linhas são
      `docs/plans/` de fases 100% concluídas (o plano da API sozinho tem 2.717
      linhas). Ninguém lê, e infla o custo de qualquer varredura de docs.
      Arquivar ou comprimir.

---

## P5 — Produto (trabalho maior, mas é o que o usuário sente)

- [ ] **Não existe histórico de exibição.** Há `RegisterVideoView.cs` com dedup por
      janela, mas nenhuma entidade/endpoint de histórico, e nenhuma rota `/history`,
      `/liked` ou `/subscriptions` no front. Lacuna mais óbvia para uma plataforma
      de vídeo.
- [ ] **Sem notificações** de vídeo novo em canal inscrito. `ChannelFollower` já
      existe; falta o resto.

---

## P6 — VidroProcessor: performance do pipeline e escalabilidade

Vindo do antigo `VidroProcessor/docs/roadmap.md`, absorvido aqui em 2026-08-30 — era um
segundo backlog do mesmo worker, sem nenhum item em comum com este arquivo. O que já estava
concluído lá não foi copiado: quem quer saber o que existe lê
`VidroProcessor/docs/agents/features-index.md`, que mapeia feature → arquivo.

**Estado em 2026-09-03:** P-PERF1 a P-PERF4 estão **implementados no código** e estavam
marcados `[ ]` aqui — mesma classe de drift do P0.1 ("documentação que mente"), agora do lado
do TODO. Corrigidos abaixo com `arquivo:linha`. **O ganho nunca foi medido:** a meta de
"~2–3 min" continua sendo estimativa de papel, ninguém rodou antes/depois (ver P-PERF6).

**Custo original:** um vídeo de ~500 MB / 1080p levava vários minutos.

### 🔴 Prioridade alta

- [ ] **P-PERF5: `WORKER_COUNT` × passos paralelos oversubscreve a CPU.**
      `main.go:64-67` — `WORKER_COUNT=0` (o default) vira `runtime.NumCPU()`. Cada job roda
      até `MAX_PARALLEL_POST_TRANSCODE_STEPS` (default **4**) processos FFmpeg em paralelo
      (`internal/processor/processor.go:196-203`), e **nenhum comando FFmpeg passa `-threads`**
      (varredura em `internal/`), então cada processo fica no modo automático — libx264 abre
      ~1,5× o número de cores em threads. Num host de 8 cores: até **32 FFmpeg simultâneos**
      disputando 8 cores.
      Não é oversight: é decisão documentada — `docs/agents/design-decisions.md` #9,
      *"FFmpeg is CPU-bound, so one worker per core is right starting point"*. A premissa é que
      o worker é a unidade de paralelismo, mas o FFmpeg **já** paraleliza internamente — e a
      decisão foi tomada **antes** de o P-PERF3 multiplicar por 4 o número de processos por
      job. Nunca foi revisitada.
      Para encode CPU-bound o default certo é 1–2 workers (ou `max(1, NumCPU/4)`), com
      `WORKER_COUNT` continuando a mandar. Mexer aqui obriga a reescrever a decisão #9 e a
      linha do `WORKER_COUNT` em `docs/agents/config.md:41`, que hoje só alerta para quota de
      CPU em container — não para a disputa entre workers do mesmo host.
      **Barato e reversível** (um número de default + duas docs), e vem **antes** de qualquer
      benchmark: medição não é interpretável enquanto os processos se atropelam.

### 🟡 Prioridade média

- [ ] **P-OPT1: tornar os passos não-críticos opcionais.** Passos 4–7 não são necessários
      para toda superfície do produto: hoje o front toca o **MP4 processado** + thumbnails —
      `GetVideo` monta `videoUrl` a partir de `ProcessedPath` e nunca expõe `hlsPath`.
      Config (flags de env) para **pular** qualquer combinação reduz tempo de FFmpeg e escrita
      no MinIO. Caminho crítico: validate → analyze → transcode → upload.
      **Coordenação com a API:** payload do webhook / `VideoArtifacts` precisa aceitar caminhos
      omitidos onde já são nullable (`HlsPath`), e o handler `VideoProcessed` + cleanup precisam
      tolerar artefato opcional faltando.

      > ⚠️ **Não leia isso como "HLS é código morto".**
      > `VidroFront/src/features/videos/components/VideoPlayer.tsx` já detecta `.m3u8` e toca via
      > `hls.js`; o player está ligado, só falta o campo na API. HLS está a uma mudança de
      > `GetVideo` de virar o caminho principal de reprodução — coloque o passo atrás de uma
      > flag, não apague.

### 🟢 Prioridade baixa

- [ ] **P-PERF6: benchmark de transcode — escopo enxuto.** `VidroProcessor/docs/TESTING.md:198`
      já lista "Transcoding + throughput benchmarks" como lacuna. O que vale: um
      `go test -bench` sobre `TranscodeVideo` com um clipe fixo de ~10s versionado, medindo
      **uma variável de cada vez** (`WORKER_COUNT`, depois `MAX_PARALLEL_POST_TRANSCODE_STEPS`),
      num host só. Serve para (a) confirmar o default escolhido no P-PERF5 e (b) finalmente
      medir o ganho de P-PERF1/2/3, que hoje é só estimativa.
      **O que não vale: matriz de perfis de hardware** (RAM × cores × GPU) para "otimizar para
      cada caso". É especulativo enquanto há um worker, um host e deploy manual — auto-scaling e
      escala horizontal estão abertos aqui embaixo, e são o pré-requisito de fazer sentido.
      E o instrumento já existe: `metrics/metrics.go` expõe
      `video_processing_step_duration_seconds{step}`, `video_processing_duration_seconds` e
      `video_size_bytes` — medição por passo, com input real e carga real. Olhar o que já está
      instalado ganha de um harness sintético, e custa zero código.

### ~~Concluído~~ ✅ — P-PERF1 a P-PERF4 *(marcados em 2026-09-03; código já estava lá)*

- [x] ~~**P-PERF1: HLS em comando único (maior impacto).**~~ `segmentForStreamingSingleCommand`
      (`internal/processor/processor-steps/streaming.go:114`) monta uma invocação só, com
      `-filter_complex` (`:134`) e um `-map` por variante — FFmpeg lê o input uma vez. O loop
      sequencial sobreviveu de propósito, como fallback (`:100`).
- [x] ~~**P-PERF2: preset de transcode `medium` → `fast`.**~~ `transcode.go:32` usa
      `-preset fast`, CRF inalterado. O caminho NVENC usa o preset de `NVENC_PRESET`
      (default `p5`).
- [x] ~~**P-PERF3: paralelizar os passos não-críticos 4–7.**~~ `runNonCriticalStepsParallel`
      (`internal/processor/processor.go:195`): semáforo dimensionado por
      `MaxParallelPostTranscodeSteps`, `sync.WaitGroup` e mutex no `result`. **Não** usa
      `errgroup` como o item previa — passo não-crítico que falha não pode cancelar os irmãos,
      que é exatamente a política que o `errgroup` aplicaria.
- [x] ~~**P-PERF4: guard rails para o pipeline otimizado.**~~ Os quatro no lugar: teto de
      FFmpeg paralelos por job (`processor.go:197-203`, com clamp em `[1,4]` — passar mais que
      4 é silenciosamente ignorado, e 4 é o número de passos); métrica por passo preservada
      (`processor.go:276`, `metrics.ProcessingStepDuration`); fallback do HLS de comando único
      para o sequencial (`streaming.go:86-97`, atrás de `HLS_SINGLE_COMMAND_FALLBACK`); e as
      quatro flags de env em `config/config.go:41-44`, documentadas em
      `docs/agents/config.md:48-51`.

      > O teto do P-PERF4 protege contra pico de CPU/RAM **dentro de um job**. Ele não vê os
      > outros workers do mesmo host — é essa lacuna que o P-PERF5 fecha.

**Longo prazo — escalabilidade:**

- [ ] **Auto-scaling:** subir workers conforme o tamanho da fila.
- [ ] **Escala horizontal:** várias instâncias do worker em máquinas diferentes.
      É o que faria a matriz de perfis de hardware descartada no P-PERF6 passar a fazer
      sentido: com uma máquina só, não faz.
- [ ] **Prioridade na fila:** vídeos curtos primeiro, longos em fila separada.

---

## Sujeira pequena

- [ ] **Limpar o que o `biome check` acusa no `VidroFront`** — 108 erros, 32 warnings e
      8 infos em 83 arquivos. É o que impede ligar o step de lint no CI (ver P1).
      **~100 dos 108 saem sozinhos** com `bunx biome check --write`: formatação em 72
      arquivos, `assist/source/organizeImports` (28) e `lint/style/useImportType` (12).
      Atenção: o `--write` também reformata `biome.json` e `.vscode/settings.json`.
      **Sobra para mão humana** (8 erros + 20 warnings), e é aqui que mora o valor:
      | regra | qtd | por quê importa |
      |---|---|---|
      | `lint/correctness/useExhaustiveDependencies` | 3 | dependência faltando em hook = bug real de stale closure |
      | `lint/suspicious/noArrayIndexKey` | 12 | `key={index}` em lista que reordena/filtra corrompe estado de componente |
      | `lint/style/noNonNullAssertion` | 11 | cada `!` é um crash em potencial — mesma classe do BUG-1 |
      | `lint/complexity/useLiteralKeys` | 6 | cosmético |
      | `lint/correctness/noUnusedImports` | 4 | cosmético |
      | `lint/correctness/noUnusedFunctionParameters` | 2 | cosmético |
      | `lint/complexity/noUselessFragments` | 1 | cosmético |
      Ordem sugerida: rodar o `--write` num commit isolado (diff enorme, zero
      comportamento), depois um segundo commit só com os `useExhaustiveDependencies` e
      `noArrayIndexKey`, que são os que podem esconder bug de verdade. Ligar o step de lint
      no `.github/workflows/ci.yml` do Front ao fechar.

- [ ] **`gofmt -l` acusa 3 arquivos no `VidroProcessor`** — `minio/client.go`,
      `minio/client_test.go`, `test/integration/pipeline_test.go`. Só formatação
      (`gofmt -w` resolve). Não quebra nada hoje porque o `ci.yml` roda `go vet` mas não
      `gofmt`; ao limpar, adicionar o check no CI (`test -z "$(gofmt -l .)"`).

- [ ] `VidroProcessor/minio/client.go:34` — `const token = "" // TODO: Ver se precisa
      adicionar esse token`. **É o único marcador TODO/FIXME/HACK/BUG em todo o
      código dos três repos** (varredura em `.cs`, `.ts`, `.tsx`, `.go`, `.json`,
      `.css`) — o código é limpo nesse aspecto; os bugs registrados estavam só nos docs.

---

## Ordem sugerida

1. ✅ ~~BUG-1~~, ✅ ~~BUG-2~~ e ✅ ~~CORS + porta 5000~~ (feitos em 2026-08-29).
   **P0 está fechado.**
2. ✅ ~~P0.1 inteiro~~ (docs mentirosas) — feito em 2026-08-29.
3. ✅ ~~P1 inteiro~~ — feito em 2026-08-29.
4. ✅ ~~Testes de `queue/`~~ (2026-08-30). Falta `processor.go` (P2) — 253 linhas, zero
   testes fora do `JobBudget`.
5. Ligar busca + ThemeToggle (P3) — features já pagas, custo quase zero.
5.1. Fixture de contrato do webhook (P2) — é a rede que teria pego o BUG-1 no ato, e agora
   custa um arquivo. Tipos gerados do OpenAPI vêm depois: mais valor, mais trabalho.
6. SEO + error boundaries (P3), CI no Front e Processor (P1).
7. README raiz + doc do fluxo ponta a ponta (P4).
8. Histórico/notificações (P5).
9. Performance do pipeline (P6) — ✅ ~~P-PERF1 a P-PERF4~~ (já estavam no código; marcados
   em 2026-09-03). O que sobrou, em ordem: **P-PERF5** (default de `WORKER_COUNT` — barato,
   reversível, e nenhuma medição vale nada antes dele), depois P-OPT1, e só então P-PERF6
   (benchmark) para medir o que P-PERF1/2/3 renderam de fato.
