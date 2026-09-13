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
*(2026-09-07)* busca ligada de ponta a ponta, `ThemeToggle` no Header, devtools fora de
produção, `typecheck` no CI do front (com os 23 erros de tipo pré-existentes zerados),
`gofmt` checado no CI do Processor, o teste template da API deletado, o **P-PERF5**
(default de `WORKER_COUNT` derivado dos cores ÷ processos FFmpeg por job), a **regra de
fail-fast vs. anomalia+ACK** escrita no `conventions.md` da API, o **Biome ligado no CI do
front** (lint + formatter, com a regra de ternário da raiz relaxada para permitir uma linha)
e o **`noUncheckedIndexedAccess`**. O workflow do front hoje roda **lint → typecheck → test →
build**, os quatro verdes.
Itens marcados `[x]` trazem o commit e o que ficou no lugar.

**Estado geral:** as features estão prontas (todas as fases do Front ✅, plano da
API 100% marcado, Processor ~95%). O que falta não é feature — é ~~(a) os três nunca rodarem
juntos~~ *(resolvido em 2026-08-29: compose único na raiz, E2E verificado)*, (b) documentação
que mente, (c) lacunas de teste na lógica de confiabilidade, (d) acabamento de UX.

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
      **Sem step de lint no Front** *(era: 109 erros do `biome check`)* — resolvido em
      2026-09-07: o workflow do front hoje roda **lint → typecheck → test → build**. Ver
      "Limpar o que o `biome check` acusa" e o item do `tsc` em Sujeira pequena.
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

- [x] ~~**`internal/processor/processor.go` (253 linhas) — zero testes.**~~ **RESOLVIDO**
      *(2026-09-07)*. 10 testes em `internal/processor/orchestration_test.go`, cobertura do
      pacote **11.1% → 78.0%**, rodando em 0,1s com passo falso — sem FFmpeg.
      Cobertos: a lista de passos não-críticos (os quatro, na ordem, com
      `nonCriticalStepCount` casando), timeout de cada passo passando por `Options.step`,
      falha de passo não-crítico que **não** para os seguintes (sequencial) nem cancela os
      irmãos (paralelo — a razão de ser `WaitGroup` e não `errgroup`), o teto de FFmpeg por job
      do P-PERF4, `runStep` (erro, timeout, cancelamento do pai, métrica por passo) e passo
      crítico abortando o job (`ProcessVideo` com arquivo inválido; pula sem `ffprobe`).
      **Custou um refactor que valia por si:** os dois orquestradores tinham **cópias
      separadas** da lista dos passos 4–7 — o risco que o `conventions.md` avisava em prosa
      ("register the step in **both** orchestrators") e que o `docs/padroes-a-importar.md`
      (item 2) queria resolver com um checker de AST. Agora existe uma lista só
      (`nonCriticalSteps`), lida pelos dois: o checker de AST deixou de ser necessário.
      Dep nova só de teste: `prometheus/client_golang/prometheus/testutil` (subpacote de dep
      que já estava lá; trouxe `kylelemons/godebug` como indireta).
      **Verificado por mutação (5×):** `continue`→`return` no sequencial, semáforo sem teto no
      paralelo, `opts.step()` removido de um timeout, passo renomeado e observação da métrica
      removida — cada um quebra um teste diferente.
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

- [~] 34 testes em 6 arquivos de API com fetch mockado **+ 4 de renderização**
      *(2026-09-13)*: `src/tests/comment-list.test.tsx` é o primeiro uso de
      `@testing-library/react`/`jsdom`, que estavam instalados e parados. Continuam sem teste de
      renderização os outros 34 componentes e as 11 rotas — o que falta agora é cobertura, não
      harness.
      **O harness custou um `vitest.config.ts` novo:** com o plugin do TanStack Start no config, o
      React resolve pela condição `react-server` e chega ao teste com o dispatcher de hooks nulo —
      todo `render` morre em `useState`. O config de teste carrega só `tsconfigPaths` + `viteReact`.
      Padrão registrado em `VidroFront/docs/agents/conventions.md` (seção "Teste de componente").
- [~] Features **`channels` e `playlists` continuam sem teste nenhum**; `comments` passou a ter os
      4 de renderização do `CommentList.tsx`, que é o componente mais complexo do app.
      Cobertos: Edit/Delete só no comentário do próprio usuário (o `isOwner`, que já esteve quebrado
      por `currentUserId` errado), comentário apagado como lápide sem ações, deslogado sem form nem
      Reply, e erro de carga.
      **Verificado por mutação:** trocar o `isOwner` por `!!currentUserId` e mostrar a linha de ações
      em comentário apagado quebram um teste cada.
      **Um bug real achado pelo quarto teste e corrigido junto:** com a query em erro o componente
      mostrava "Failed to load comments." **e** "No comments yet." ao mesmo tempo — a mesma mentira
      ao usuário que o `dashboard.tsx` tinha em 2026-09-07, e a mesma correção (`hasNoComments`
      nomeado, excluindo `isError`).

### VidroApi — bom, só limpar

- [ ] Excelente no geral: 294 `[Fact]`/`[Theory]` *(recontado em 2026-09-07; 327 testes
      rodando, 82 unit + 245 integração)*, 43 arquivos de integração para
      43 features (≈1:1 por endpoint, com testcontainers). Nada a fazer além de:
- [x] ~~Deletar `tests/VidroApi.UnitTests/UnitTest1.cs`~~ — template vazio do
      `dotnet new`, um `[Fact]` de corpo vazio que sempre passa. **RESOLVIDO** *(2026-09-07)*.
      327 testes continuam passando (82 unit + 245 integração).

### Nos três

- [ ] Nenhum teste E2E do fluxo que define o produto: upload → processamento → play.
      Cada repo testa a própria borda; a integração entre eles não é testada.
- [ ] **Nenhum teste de carga HTTP da API.** O que existe anotado de performance é só
      transcode (P-PERF6), dentro do worker. Ninguém nunca mediu a API sob concorrência:
      latência e taxa de erro por endpoint, pool do Npgsql, e o rate limit de
      `SignIn`/`SignUp` (`AddRateLimiter`, P1) sob pressão — este último nunca foi exercido
      com carga real, só com teste de integração.
      ~~**Pré-requisito:** F5 — o Prometheus só raspa `worker:8080`.~~ **Destravado**
      *(2026-09-13)*: a API expõe `/metrics` e o Prometheus raspa `api:5000` (degrau 2 do
      `docs/observabilidade.md`). Latência por rota, taxa de erro e pool do Npgsql já são
      observáveis durante a carga; `aspnetcore_rate_limiting_*` só aparece quando alguma
      requisição passa pela política `auth` — ou seja, é esta carga que vai exercitá-lo.
      **Escopo enxuto quando for a hora:** um cenário só — upload → `GetVideo` em polling —
      contra o compose local, num host só. Nada de matriz de perfis; vale a mesma ressalva do
      P-PERF6, com um worker e deploy manual isso é especulativo.

### Contrato entre serviços — destravado pelo monorepo (2026-08-30)

Os dois P0 desta lista foram divergência de contrato entre serviços. Enquanto eram três repos,
não havia onde colocar a rede que os pegaria. Agora há. Ver `docs/MONOREPO.md`, seção "O que a
migração destrava".

- [~] **Gerar os tipos do front a partir do OpenAPI da API.** *(parcial — ver o bloco no fim do item)*
      `VidroFront/src/shared/types.ts:64-69` espelha **na mão** seis enums do backend
      (`VideoStatus`, `VideoVisibility`, `ReactionType`, `PlaylistVisibility`, `PlaylistScope`,
      `CommentSortOrder`), e as shapes de request/response de cada feature são redigitadas em
      `features/*/types.ts`.
      **Conferi os seis: batem com o backend hoje** — cinco contra `VidroApi/src/VidroApi.Domain/Enums/`
      e `CommentSortOrder` contra `Features/Comments/ListComments.cs:19` *(o enum mudou para
      `Domain/Enums/CommentSortOrder.cs` em 2026-09-09)*. O problema não é estarem
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

      > **PARCIALMENTE RESOLVIDO** *(2026-09-09)* — e o achado é que **o item, como estava escrito,
      > não era alcançável**: montei a geração e o documento não descreve o que interessa.
      >
      > `Microsoft.Extensions.ApiDescription.Server` gera o documento no build, e ele sai assim:
      >
      > | | resultado |
      > |---|---|
      > | rotas / métodos / parâmetros | **43 de 43**, corretos |
      > | operações com schema de **resposta** | **0 de 43** — os handlers devolvem `IResult`, sem `TypedResults`/`.Produces<T>()` |
      > | schemas de request | 16 operações → **1 schema só**, chamado `Request` (o record aninhado de cada slice colide) |
      > | os seis enums | `VideoVisibility` e `CommentSortOrder` viram `{"type":"integer"}` **sem valores**; os outros quatro nem aparecem |
      >
      > Gerar `.d.ts` disso hoje **não pegaria o drift que motiva o item**. Então o item virou dois
      > portões, cada um fechando um drift real:
      >
      > 1. **`contracts/enums.json`** — golden dos seis enums, mesmo padrão do webhook, lido pelos
      >    dois lados: `VidroApi/tests/.../Contracts/EnumContractTests.cs` (reflexão; sem container,
      >    não usa `IClassFixture<ApiFactory>`) e `VidroFront/src/tests/enum-contract.test.ts`.
      >    Os dois também falham se o golden listar enum que aquele lado não espelha mais.
      >    **Verificado por mutação:** trocar `"Like": 1` por `0` no golden deixa os dois vermelhos.
      > 2. **`VidroApi/openapi/v1.json` versionado** + passo no `api.yml` que regenera e falha no
      >    diff. Trava as 43 rotas — a classe do P0.1 ("4 rotas erradas no `features-index`").
      >    **Verificado por mutação:** renomear `/v1/feed` para `/v1/feeeed` produz diff.
      >
      > Custos e pegadinhas, todos documentados em `VidroApi/openapi/README.md` e em
      > **design-decisions #11** da API:
      > a geração fica atrás de `-p:GenerateOpenApiDocument=true` (boota o host; build de todo dia
      > não paga isso); precisa de `ASPNETCORE_ENVIRONMENT=Development` porque `AddInfrastructure`
      > monta o client do MinIO na hora e o endpoint só existe no `appsettings.Development.json`;
      > e o `Program.cs` **pula a migration** quando o processo é o `GetDocument.Insider`, porque
      > gerar contrato não pode exigir Postgres de pé.
      > De quebra: os três workflows passaram a disparar em `contracts/**` — sem isso o golden
      > (o novo **e** os do webhook, que já estavam nessa situação) podia mudar sem CI nenhum ver.
      >
      > **O que continua aberto** para o item fechar de verdade: a API declarar as respostas
      > (`TypedResults`/`.Produces<T>()` nas 43 features), schema ID por feature para resolver a
      > colisão de `Request`, e um transformer para os enums saírem com valores. Só então
      > `openapi-typescript` + a camada fina do envelope `{ data: T }` fazem sentido.

- [x] ~~**Fixture de contrato compartilhada entre Processor e API para o webhook.**~~
      **RESOLVIDO** *(2026-09-07)*. Quatro golden em `contracts/` (`video-processed-*.json`),
      lidos pelos dois lados: `VidroProcessor/webhook_contract_test.go` prova que
      `buildWebhookPayload` serializa **exatamente** aquilo, e `VideoProcessedTests.cs` faz POST
      assinado do **mesmo arquivo** no endpoint real. Casos: sucesso completo, sucesso sem nenhum
      artefato opcional e sem bloco de metadata, sucesso sem `processedPath` (→ `Failed`) e falha
      permanente. O `videoId` é um placeholder que cada lado troca pelo id do seu teste.
      Os payloads escritos à mão no teste da API foram deletados — os 6 testes que usavam
      `BuildSuccessPayload` agora exercitam o contrato de verdade.
      No caminho, `notifyWebhook` teve o mapeamento `JobState` → `Payload` extraído para
      `buildWebhookPayload` (o resto era envio, não dava para testar sem servidor).
      **Verificado por mutação dos dois lados:** renomear o `json:` tag de `previewPath` quebra o
      teste Go; renomear `processedPath` no golden quebra o teste C#.
      **Falta o resto do contrato, hoje só documentado:** nome da fila
      (`JobQueueSettings:QueueName` ↔ `PROCESSING_REQUEST_QUEUE`), `callback_url` no `JobState`,
      e o layout de paths no MinIO.

---

## P3 — UI / UX do frontend

**O que já está bom (não mexer):** estados consistentes — skeletons em 7 telas,
empty states em 12 lugares com texto próprio, `isPending` tratado em 23 arquivos,
toasts (`sonner richColors`), forms com react-hook-form + zod, shadcn/ui coerente.

### Features construídas e desligadas

- [x] ~~**Busca é uma feature morta de ponta a ponta.**~~ **RESOLVIDO** *(2026-09-07)*.
      `searchVideos` + `useSearchVideos` (cursor, `SEARCH_LIMIT` 20), `search.tsx` lê a query
      de `?q=` via `validateSearch` e renderiza o `VideoGrid` com "Load more", e o `Header`
      ganhou um `<search>` com o campo. **Atenção ao contrato:** `limit` é obrigatório no
      endpoint (`SearchVideos.cs`, `int limit` sem default) — omitir dá 400.
- [x] ~~**`src/components/ThemeToggle.tsx` é código morto**~~ **RESOLVIDO** *(2026-09-07)*.
      Plugado no `Header`. O componente vinha de outro projeto e estilizava com
      `--chip-bg`/`--chip-line`/`--sea-ink`, que **não existem** no `styles.css` — teria
      renderizado sem estilo. Refeito com o `Button` do shadcn + ícone (`Sun`/`Moon`/`Monitor`).
      Fica de dívida o flash claro no primeiro paint (o tema só é aplicado depois da
      hidratação); resolver exige script inline no `<head>` do `shellComponent`, marcado com
      `ponytail:` no arquivo.

### Acabamento

- [x] ~~**Header não é responsivo**~~ **RESOLVIDO** *(2026-09-07)*. Só CSS, sem drawer nem
      componente novo: (a) rótulo de texto vira `sr-only` até **lg** — `sr-only`, não `hidden`,
      para o nome acessível continuar no leitor de tela — e o `Dashboard`, que era só texto,
      ganhou ícone (`LayoutDashboard`); (b) abaixo de **sm** a busca sai da primeira linha e
      ocupa a segunda inteira (`flex-wrap` + `basis-full` + `order-last`), com o header em
      `min-h-14 py-2` em vez de `h-14`.
      **Medido no browser** (agent-browser, header autenticado, o caso mais cheio) — a lição do
      `docs/padroes-a-importar.md` item 17 é exatamente esta, layout quebrado não falha teste de
      conteúdo:
      | largura | overflow | altura do header | largura da busca |
      |---|---|---|---|
      | 320 | não | 89px | 288px |
      | 375 | não | 89px | 343px |
      | 640 | não | 57px | 313px |
      | 1024 | não | 57px | 402px (rótulos aparecem) |
      | 1280 | não | 57px | 448px |
      **A primeira tentativa passava e estava errada:** só escondendo rótulo a partir de `sm`
      não havia overflow em lugar nenhum, mas a busca sobrava com **17px** em 320px e **18px**
      em 640px — cabia porque o campo era esmagado. É por isso que a asserção é a largura da
      busca, não só `scrollWidth <= clientWidth`.
- [ ] **`<html lang="pt-BR">` com a UI toda em inglês** (`src/routes/__root.tsx:76`),
      datas com `toLocaleDateString('en-US')` (`watch.$videoId.tsx:38`), e o Header
      mistura "Upload"/"Dashboard"/"Sign out" com **"Meu Perfil"**. Escolher um
      idioma — `lang` errado também é bug de leitor de tela.
- [ ] **SEO é zero.** Só `__root.tsx` define `head`, então toda página é
      `<title>Vidro</title>`, sem `description`, sem OG, sem Twitter card.
      Compartilhar link de vídeo não gera preview. Com SSR já funcionando, é jogar
      fora o motivo de ter SSR.
- [x] ~~**Nenhum `errorComponent` ou `notFoundComponent` em nenhuma rota.**~~ **RESOLVIDO**
      *(2026-09-07)*. Em vez de 11 rotas, **um lugar**: `src/router.tsx` registra
      `defaultErrorComponent` e `defaultNotFoundComponent` (`components/RouteFallback.tsx`), que
      toda rota herda — uma rota só declara os seus quando consegue fazer melhor. O fallback de
      erro tem "Try again" (`router.invalidate()`) + link para a home, e mostra `error.message`
      só em `import.meta.env.DEV`.
      **Verificado rodando:** `/a/b/c/d` devolve a tela de "Page not found" já no SSR. O
      fallback de *erro* não dá para conferir por `curl`: um throw no render durante SSR faz o
      React trocar para render no cliente (`Switched to client rendering because the server
      rendering errored`), então quem renderiza a tela é o browser.
      **Achado no caminho:** `/rota-que-nao-existe` **não** é 404 — casa com `/$username`, que
      renderiza "rota-que-nao-existe's channels". Rota de um segmento sempre vai cair no
      `$username`; 404 de usuário inexistente é o `isError` daquela rota, não o do router.
- [x] ~~`index.tsx` (home) e `dashboard.tsx` tratam `isPending` mas não `isError`.~~
      **RESOLVIDO** *(2026-09-07)*. Home: `FeedSection` e `TrendingSection` com mensagem própria.
      Dashboard: **era mentira na cara do usuário** — com a query falhando, `isPending` fica
      falso e `playlists.length === 0`, então aparecia "No personal playlists yet." em vez de
      erro. Agora `playlistsFailed` tem branch própria e o empty state exige `!playlistsFailed`
      (via `hasNoPlaylists`/`hasPlaylists`, nomeados em vez de compostos inline).
- [x] ~~**Devtools vão para produção**~~ **RESOLVIDO** *(2026-09-07)* — `<TanStackDevtools>`
      agora atrás de `import.meta.env.DEV` em `__root.tsx`.
- [~] **A11y rasa** — só ~10 arquivos têm qualquer `aria-*`/`alt`/`sr-only`.
      *(2026-09-07: o Header deixou de ser o pior caso — `SearchBox` e `ThemeToggle` têm
      `aria-label`, e a busca usa o elemento nativo `<search>`. O resto do app continua raso, e
      o `<video>` sem `<track>` virou item de P5.)*
      *(2026-09-13: fechados os controles cujo **ícone era o único significado**.* Os 6 botões de
      reação (`CommentList`, `ReplyList`, `watch.$videoId`) tinham nome acessível vazio quando o
      contador era zero — "botão, botão, botão" no leitor de tela — e no `watch` o nome era só o
      número. Ganharam `sr-only`, o padrão que o Header já usava. As 6 estatísticas do `VideoCard`
      (views/likes/dislikes) eram número solto: agora leem "12 views".
      Travado por teste: `comment-list.test.tsx` consulta os botões por nome acessível
      (`Like`, `Dislike 3`) — **verificado por mutação**, remover um `sr-only` deixa vermelho.
      *Os botões só-ícone de verdade (`Trash2` em `dashboard`/`PlaylistItemList`, `Pencil`/
      `ImagePlus` no `VideoCard`) já tinham `aria-label`.*
      **Continua aberto:** ninguém rodou um audit (axe/Lighthouse) no app; o que foi feito aqui
      saiu de leitura de código, não de ferramenta.)

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
- [x] ~~**60% do markdown do projeto é arqueologia.**~~ **RESOLVIDO** *(2026-09-13)*.
      `VidroApi/docs/plans/` e `VidroFront/docs/plans/` deletados: **5.194 linhas**, 8 arquivos,
      zero checkbox em aberto nos oito. O histórico continua no git; o que some é o custo de
      varrer doc de fase concluída em toda tarefa.
      Ponteiros ajustados na mesma leva (`VidroApi/CLAUDE.md`, `VidroFront/CLAUDE.md`,
      `design-decisions.md #10`) — doc que aponta para arquivo inexistente é a mesma classe do P0.1.
      **Duas coisas foram resgatadas antes de apagar**, porque eram futuro e não história:
      o multipart upload com resume (virou item de P5 aqui embaixo) e as notificações, que já
      estavam em P5. O resto da seção "Melhorias Futuras" do design da API já tinha sido feito
      (busca) ou já mentia (`MediatR`, que o repo nunca usou — é o `Mediator` com source generator).

---

## P5 — Produto (trabalho maior, mas é o que o usuário sente)

- [ ] **Não existe histórico de exibição.** Há `RegisterVideoView.cs` com dedup por
      janela, mas nenhuma entidade/endpoint de histórico, e nenhuma rota `/history`,
      `/liked` ou `/subscriptions` no front. Lacuna mais óbvia para uma plataforma
      de vídeo.
- [ ] **Sem notificações** de vídeo novo em canal inscrito. `ChannelFollower` já
      existe; falta o resto.
- [ ] **Upload é um PUT presignado só, sem resume.** Um vídeo grande que perde a conexão
      recomeça do zero. O caminho previsto desde o design da API é a S3 Multipart API com o
      estado das partes no `localStorage` do cliente; atravessa API (`design-decisions.md #10`)
      e front. Enquanto o produto não tiver upload grande de verdade, é especulativo.
- [ ] **Nenhuma legenda/caption em lugar nenhum.** O pipeline não tem step de legenda, a API
      não tem campo, e o `<video>` do `VideoPlayer.tsx` sai sem `<track>` — hoje suprimido com
      `biome-ignore lint/a11y/useMediaCaption` **nomeando a lacuna**, não fingindo que não existe.
      Para quem não ouve, todo vídeo da plataforma é inacessível. Atravessa os três serviços.

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

- [x] ~~**P-PERF5: `WORKER_COUNT` × passos paralelos oversubscreve a CPU.**~~ **RESOLVIDO**
      *(2026-09-07)*. `processor.DefaultWorkerCount(numCPU, parallelSteps, maxParallelSteps)`
      = `numCPU / processos FFmpeg por job`, piso 1; `workerCount(cfg)` no `main.go` (mesmo
      padrão de `jobTimeout`/`JobBudget`). Num host de 8 cores: **2 workers** em vez de 8, e
      ~8 FFmpeg simultâneos em vez de até 32. Desligar `PARALLEL_NON_CRITICAL_STEPS` ou baixar
      `MAX_PARALLEL_POST_TRANSCODE_STEPS` para 1 devolve 1 worker por core sozinho — nenhum
      knob novo. `WORKER_COUNT > 0` continua mandando.
      O clamp `[1,4]` que estava inline em `runNonCriticalStepsParallel` virou
      `clampParallelSteps`, compartilhado com o default: teste que copia a condição não pega
      drift (mesma lição dos testes de `queue/`).
      Docs reescritas: decisão **#9** (título, âncora e índice), `config.md:41`,
      `architecture.md:27`, `features-index.md:9`, `GETTING_STARTED.md:213`.
      Testes: `internal/processor/workers_test.go` — a invariante é
      `workers × passos paralelos ≤ cores`. **Verificado por mutação:** trocar
      `processesPerJob` por `1` quebra 2 dos 4 testes.
      **Continua não medido:** o número é raciocinado, não medido — é o P-PERF6 que confirma.

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

- [x] ~~**Limpar o que o `biome check` acusa no `VidroFront`**~~ **RESOLVIDO** *(2026-09-07)*.
      Zero erros; `bun run lint` (`biome ci`) roda no `.github/workflows/front.yml`.
      **Custou uma decisão de convenção.** Dos 109 erros, **72 eram "arquivo precisa de
      formatação"**, e o formatter do Biome **colapsa ternário curto em uma linha**, contra a
      regra do `CLAUDE.md` da raiz que valia para os três serviços — sem opção no Biome para
      preservar. Resolvido **relaxando a regra**: ternário curto passou a caber em uma linha
      nos três serviços, e o formatter ficou ligado com a config alinhada ao código
      (2 espaços, aspas simples, sem ponto e vírgula). 62 arquivos reformatados num commit
      mecânico, mais 33 de ordem de import e `useImportType`.
      Os 8 de mão: os 3 `noArrayIndexKey` eram `biome-ignore` **mal posicionado** (a suppression
      vale só para a linha seguinte e o `key={i}` caía duas abaixo); os 3
      `useExhaustiveDependencies` estavam "suprimidos" por um `// eslint-disable-next-line` —
      **comentário morto, este repo não usa ESLint** — e viraram dependência honesta
      (`loadedVideoId` no corpo do efeito, porque navegar entre vídeos reusa a rota em vez de
      remontar); 1 `noStaticElementInteractions` é falso positivo (preview decorativo no hover,
      o card já é `Link` focável) e 1 `useMediaCaption` é **lacuna real**, ver P5.
      Sobram 11 `noNonNullAssertion` e 7 infos, todos **warning** — não quebram o CI. Cada `!`
      continua sendo um crash em potencial (mesma classe do BUG-1); fica como item próprio.
      **Duas armadilhas do Biome que custaram tempo e estão documentadas no `CLAUDE.md` do
      front:** comentário `//` no `biome.json` quebra a config **em silêncio** (cai no default
      e passa a lintar `dist/`: 83 → 148 arquivos), e `biome-ignore` com motivo em duas linhas
      vira "unused suppression" sem aplicar a regra.

- [x] ~~**`noUncheckedIndexedAccess` desligado no front**~~ **RESOLVIDO** *(2026-09-07)*.
      Ligado no `tsconfig.json`. **Custou 4 correções, todas em teste** — o código de produção
      passou limpo de primeira porque já tratava índice com `?? undefined`/`?.`
      (`video.thumbnailUrls[0] ?? undefined`). Os 4 eram `mockFetch.mock.calls[0]`
      destruturado direto; viraram guard com mensagem própria em vez de `!`, que o `biome`
      acusaria. Verificado que a flag morde de fato (um `const first: string = items[0]`
      temporário quebra o build). Regra escrita em
      `VidroFront/docs/agents/conventions.md`.

- [x] ~~**Os 11 `noNonNullAssertion` que sobraram no front.**~~ **RESOLVIDO** *(2026-09-13)*.
      Zero `noNonNullAssertion` no `biome check`. Os 9 de `queryFn` viraram `skipToken` — o
      `enabled: !!x` saiu junto, porque `skipToken` já deixa a query no mesmo estado, e agora é o
      **tipo** que garante o parâmetro em vez de uma promessa ao compilador. Regra registrada em
      `VidroFront/docs/agents/conventions.md`.
      Os 2 de `CommentList`/`ReplyList` eram outra coisa: `content` é `string | null` de verdade
      no contrato (comentário apagado), e o `!` estava num ramo que **não** é o de `isDeleted` —
      viraram `?? ''`.
      34 testes passando, `typecheck` e `build` limpos.

- [x] ~~**Nada rodava `tsc --noEmit` no front** — nem script, nem CI.~~ **RESOLVIDO**
      *(2026-09-07)*. `"typecheck": "tsc --noEmit"` no `package.json` + passo **Typecheck** no
      `.github/workflows/front.yml`, antes do test.
      **A doc subestimava:** `docs/padroes-a-importar.md` (item 15) dizia "uma linha cada" —
      o compilador acusava **23 erros pré-existentes** em 11 arquivos, todos zerados junto:
      8 de símbolo não usado, 5 de `ReactionType` (const, não tipo) usado como tipo — daí o
      novo `ReactionTypeValue` em `shared/types.ts` —, 6 de generics de `react-hook-form`
      (`z.coerce.number()` produz input `unknown` no zod 4; virou `z.number()`, já que os
      dois `setValue` afetados já convertiam com `Number()`), 2 de narrowing que não
      sobrevive dentro de closure em `watch.$videoId.tsx`, e 1 **bug real**:
      `currentUserId={currentUser?.id}` — `UserProfile` tem `userId`, não `id`, então o
      `isOwner` do `CommentList` era sempre falso e ninguém via os botões de editar/apagar
      no próprio comentário. Era exatamente o tipo de erro que o portão existe para pegar.

- [x] ~~**Nenhum linter no `VidroProcessor`**~~ **RESOLVIDO** *(2026-09-07)*. `.golangci.yml`
      com 15 linters, cada um sendo uma regra que já estava escrita em prosa no
      `conventions.md`: `errorlint` (`%w`), `nilerr`, `forbidigo` (`os.Getenv` só no
      `config/config.go`, `time.Now` fora dos passos), `depguard` (zerolog no worker; passo de
      pipeline não importa MinIO/Redis), `godox` (FIXME/HACK/XXX barrados, **TODO liberado**),
      `noctx`, `bodyclose`, `nolintlint` + os baratos. Formatters `gofumpt` + `goimports`.
      O passo **Lint** do `processor.yml` substituiu os passos de `gofmt` e `go vet`.
      **Custou 74 achados**, todos zerados. Os que eram bug de verdade e não estilo:
      - `queue/client.go` fazia `result.(*Message)` sem checar — asserção crua no retorno
        `any` do circuit breaker, ou seja, panic no worker se o tipo mudasse.
      - 5 `fmt.Errorf(... %v, err)` em `main.go` quebravam o `errors.Is` de quem chamasse, e
        um `err != context.Canceled` no loop do worker falharia com erro embrulhado.
      - `streaming.go:258` descartava a saída da primeira tentativa NVENC (`ineffassign`).
      - `main.go` engolia o erro de `shutdownTracing` no `defer`; agora loga.
      O resto foi mecânico: 16 de formatação, 13 `//nolint:errcheck` que ficaram redundantes
      em teste, 5 `intrange`, `os.Remove`/`w.Write` com `_ =` explícito, `exec.Command` →
      `CommandContext(t.Context())` no helper de teste e `http.NewRequestWithContext` no
      webhook (com o motivo de ser `context.Background()` escrito no código).
      Único `//nolint` novo: `BRPopLPush` (SA1019), que é decisão documentada (#1).

- [x] ~~**O `context` do job não atravessa `queue/` e `minio/`**~~ — achado pelos 16 avisos de
      `contextcheck`, que por isso ficou **desligado** no `.golangci.yml`.
      As funções públicas dos dois pacotes (`GetJobState`, `SetJobFailed`, `HealthCheck`,
      `GetQueueSize`, `DownloadVideo`, `UploadVideo`, `UploadDirectory`, `ArchiveRawVideo`,
      `PublishSuccessMessage`...) não aceitam `context.Context` e usam `context.Background()`
      por dentro. Consequência real: quando o orçamento do job estoura ou o worker recebe
      `SIGTERM`, o cancelamento **não chega** ao Redis nem ao MinIO — o download de 500 MB
      segue até o fim, e o teto de 30s do shutdown gracioso (design-decisions #12) conta com
      operações que não sabem que devem parar.
      É refactor de assinatura pública dos dois pacotes (+ call sites no `main.go`), não
      limpeza de lint. Quando estiver feito, religar o `contextcheck`.

      > **RESOLVIDO** *(2026-09-09)*. As 11 funções públicas do `queue/` e as 6 do `minio/`
      > recebem `context.Context` como primeiro parâmetro e o repassam ao Redis/MinIO;
      > `contextcheck` está **ligado** no `.golangci.yml`, e dos 16 achados sobraram **2**, os
      > dois `go notifyWebhook` — detachado de propósito, com `//nolint:contextcheck` nomeando o
      > motivo (o webhook sai de um defer, quase sempre com o contexto do job já cancelado;
      > quem limita é o timeout de 10s do próprio client HTTP).
      >
      > **A armadilha, e é a parte que importa:** fechar o job (`SetJobFailed`, `RequeueJob`,
      > `MoveToDLQ`, `AcknowledgeMessage`, `SetJobDone`) é exatamente o que **tem** que acontecer
      > *depois* do cancelamento. Rodar isso no contexto cancelado deixaria o vídeo preso em
      > `:processing` para sempre — pior que o bug original. Então `processNextMessage` monta um
      > segundo contexto com `context.WithoutCancel(ctx)` + 10s (`bookkeepingTimeout`) e usa esse
      > para toda escrita de estado e para o ack. O I/O pesado (download, transcode, uploads,
      > publish de sucesso) continua no `processCtx`, que **deve** ser cancelável.
      > `WithoutCancel` preserva os valores — o logger do job junto — e derruba só o cancelamento.
      > Regra registrada como **design-decisions #13**.
      >
      > Achado no caminho: o `//nolint:staticcheck` do `BRPopLPush` estava no fim da linha e, com
      > o `contextcheck` ligado, o `nolintlint` passou a acusá-lo de "unused" **mesmo suprimindo
      > o SA1019 de verdade** (conferido removendo o directive: o SA1019 volta). Mover o directive
      > para a linha de cima resolve — e ficou mais legível.
      >
      > Testes: `TestQueueOperations_StopOnCanceledContext` (as 11 entradas públicas, uma
      > por subteste) e `TestRecoverStuckJobs_StopsOnCanceledContext`. Cobertura do `queue`
      > **60.3% → 75.8%**. **Verificado por mutação:** voltar o `client.Get` do `GetJobState`
      > para `context.Background()` quebra o subteste de `GetJobState`.

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
5. ✅ ~~Ligar busca + ThemeToggle~~ (P3) — feito em 2026-09-07, junto com devtools fora de
   produção, `gofmt` no CI do Processor, o teste template da API deletado e os três portões do
   front no CI (**lint → typecheck → test → build**, com `noUncheckedIndexedAccess` ligado).
5.1. ✅ **Fila combinada de 2026-09-07 fechada inteira:** fixture de contrato do webhook,
   testes do `processor.go`, `.golangci.yml` no Processor,
   `errorComponent`/`notFoundComponent` + `isError`, e Header responsivo.
   **Próximo daqui:** tipos gerados do OpenAPI (mais valor, mais trabalho) e o `context` que não
   atravessa `queue`/`minio` (ver "Sujeira pequena").
   Tipos gerados do OpenAPI vêm depois da fixture: mais valor, mais trabalho.
6. SEO + idioma (P3). **SEO não é acabamento:** exige `head` por rota nas 11 rotas e decidir o
   idioma antes (`<html lang="pt-BR">` com a UI em inglês) — é trabalho médio.
7. README raiz + doc do fluxo ponta a ponta (P4).
8. Histórico/notificações (P5).
9. Performance do pipeline (P6) — ✅ ~~P-PERF1 a P-PERF4~~ (já estavam no código; marcados
   em 2026-09-03) e ✅ ~~P-PERF5~~ (2026-09-07). Sobrou **P-OPT1** e, depois dele, **P-PERF6**
   (benchmark) — que agora finalmente vale a pena: com o P-PERF5 fechado, os processos não se
   atropelam mais e a medição passa a ser interpretável.
