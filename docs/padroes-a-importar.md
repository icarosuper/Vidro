# Padrões a importar de outros repos

Levantamento feito em 2026-08-31 sobre quatro repositórios de referência, procurando regra
ou ferramenta que já se provou lá e que faz falta aqui.

| Repo de referência | Onde fica | O que se aproveita |
|---|---|---|
| `AutoAnimeDownloader` | `~/Projetos/AAD/AutoAnimeDownloader` | padrões de teste em Go, testes de geometria no front |
| `mamut-api` | `~/Projetos/Mammute/mamut-api` | `.golangci.yml`, checker de AST próprio, teste de contrato |
| `MT` (microsserviços de pagamento) | `~/Projetos/MT` | convenções de .NET: logging, testes, anti-padrões |
| — | — | frontend: sugestões próprias, não há repo de referência |

## Ponto de partida

**As convenções de documentação já foram importadas.** `docs/agents/` com
architecture/conventions/config/design-decisions/features-index, decisões numeradas com
âncora, checklists no fim do `conventions.md`, a regra do "código ruim no caminho",
commit em português — tudo isso já está nos três serviços, e veio do AAD e do mamut-api.
Nada disso precisa ser refeito.

**O que falta é enforcement.** Toda regra do Vidro é prosa num `.md` que o agente pode ou
não ler. AAD, mamut-api e MT convertem regra em coisa que a máquina cobra: linter, script
de AST, teste. É aí que está o ganho.

---

## Go — VidroProcessor

### 1. `.golangci.yml` (do mamut-api) — maior ganho isolado

`VidroProcessor/docs/agents/conventions.md` diz textualmente *"Standard `gofmt` / `go vet`.
No custom linter."* O CI roda `go build`, `go vet`, `go test`. O mamut-api tem um
`.golangci.yml` de ~250 linhas onde cada linter **é** uma regra de `conventions.md` que
virou build vermelho.

Mapeamento direto entre regra que o Vidro já escreveu e o linter que a cobra:

| Regra já escrita no Processor | Linter |
|---|---|
| `fmt.Errorf("...: %w", err)`, nunca `%v` | `errorlint` |
| sem `os.Getenv` fora de `config/config.go` | `forbidigo` + `path-except` |
| sem stdlib `log` no worker (exceto `config.go`) | `depguard` negando `log` |
| toda chamada externa com ctx/timeout | `noctx`, `contextcheck`, `bodyclose` |
| erro não-crítico é engolido **de propósito** | `nilerr` (pega o `return nil` com err vivo que *não* foi de propósito) |
| "só deixar um TODO" é opção válida da regra da raiz | `godox` com `FIXME`/`HACK`/`XXX` barrados e **`TODO` liberado** |

O `godox` assim configurado é uma escolha deliberada que o mamut-api documenta dentro do
próprio yml: TODO marcado na linha do código não drifta como item de doc externa;
FIXME/HACK/XXX sinalizam código errado que subiu assim, e isso precisa de justificativa.

**`depguard` para congelar a fronteira arquitetural:** verificado em 2026-08-31 que
`internal/processor/processor-steps/*.go` **não importa nada de minio/redis** — a regra
"toda chamada externa vai por circuit breaker" já é verdade de fato. Um bloco de `depguard`
negando esses imports em `processor-steps/**` transforma um fato acidental em invariante.

Formatters do mamut-api que valem junto: `gofumpt`, `goimports`, `gci` com
`prefix(video-processor)`.

> Não importar do AAD: a ordem de import dele (internos primeiro, depois stdlib, depois
> terceiros) é não-padrão e briga com o `gci`.

### 2. Checker de AST próprio quando nenhum linter cobre (`make check-ownership`, do mamut-api)

O mamut-api tem `scripts/checkownership` + alvo de Makefile **obrigatório** no ciclo
pós-mudança, porque a classe de bug que ele guarda (IDOR) não tem linter. O padrão
generalizável: **regra que dói e falha em silêncio ganha um script de AST de ~100 linhas em
vez de mais um parágrafo de doc.**

Dois candidatos no Vidro, ambos falhas silenciosas que a própria doc já admite:

- `VidroProcessor/docs/agents/conventions.md`, checklist de pipeline step, passo 4:
  *"Register the step in **both** orchestrators [...] A step wired into only one runs only
  under one value of `PARALLEL_NON_CRITICAL_STEPS`."* Um AST que compara as duas listas
  (`runNonCriticalStepsSequential` e `runNonCriticalStepsParallel`) fecha isso.
- `VidroApi/docs/agents/conventions.md`, checklist de feature slice, passo 2: *"A wrong
  signature does not fail the build — the route simply never exists, and the symptom is a
  404 in a test."*

O mamut-api também documenta as limitações do próprio checker no cabeçalho do script
(é best-effort, não prova), o que evita falsa sensação de cobertura. Vale copiar o hábito.

### 3. Teste de contrato por AST (`access_test.go`, do mamut-api) — o mais relevante daqui

O mamut-api tem um teste que lê os `huma.go` **por AST** e confere a classe de acesso
declarada contra uma tabela `classeEsperada` versionada — mexer na tabela é o que faz a
mudança de segurança aparecer no diff. Ele lê AST e não o OpenAPI de propósito, porque rota
com `Hidden: true` não entra na spec.

O análogo aqui é direto: os **dois P0 do `TODO.md` foram divergência de contrato entre o
webhook Go e o handler C#**, e ninguém viu por meses. O monorepo permite um teste que lê o
struct do payload em Go e o `Request` em C# e falha quando os campos divergem. É a peça que
o `docs/MONOREPO.md` promete e que hoje só existe como disciplina.

### 4. Fake tipado, nunca mock (do mamut-api)

O mamut-api tirou `testify/mock` do repo inteiro e documenta o motivo concreto: mock casa
argumento **por valor em runtime**, então quando o tipo de um id mudou de `uint` para
`uuid.UUID` as expectativas pararam de casar e os testes quebraram **calados**, sem erro de
compilação. Fake com campo tipado quebra no build. Vale como regra escrita nos três
serviços.

### 5. Nunca deixar trabalho de fundo sobreviver ao teste (do AAD)

`docs/agents/testing.md` do AAD tem a seção *"Never Let Background Work Outlive Its Test"*,
com tabela de "entry point → como esperar" e a descrição dos sintomas: o `-race` culpa um
teste inocente, e a falha só aparece sob `-count=5`.

O Processor é um worker com pool de goroutines e testes de integração com testcontainers —
mesma classe de risco, e não há nada escrito sobre isso.

### 6. Teste-instrumento que nunca falha, só mede (do AAD)

`live_pack_measure_test.go`: gated por variável de ambiente, *"não é teste de regressão — é
instrumento de medição"*, só imprime. Foi o que produziu os números que fundamentaram duas
decisões do repo.

O P6 do `TODO.md` é exatamente "performance do pipeline FFmpeg (P-PERF1..4, P-OPT1)". Um
teste-instrumento gated por `VIDRO_LIVE_FFMPEG=1` que mede tempo por step em vídeo real é a
ferramenta para atacar esse P6 com número em vez de palpite.

---

## .NET — VidroApi

### 7. `conventions.md` da API não fala **nada** de logging (do MT)

O Processor tem seção de logging com níveis; a API não tem uma linha. O MT tem a seção mais
madura dos quatro repos:

- Prefixo `[{Feature}]` como primeiro placeholder, **sempre via `nameof(...)`** — nunca
  string hardcoded, nunca concatenação com `+`, um template único.
- Exception é o **1º argumento** de `LogError`, nunca `.ToString()` embutido no template.
- `{Nome}` para escalar; `{@Nome}` (destructuring) só quando serializa objeto inteiro.
- Níveis com semântica explícita: `Critical` = terminal/irrecuperável (segurança, cripto,
  corrupção); `Error` = exception real ou falha que bloqueia; `Warning` = anomalia não
  bloqueante; `Information` inclui **erro de negócio esperado** (não vira Warning só por ser
  caminho de falha); `Debug` = payload verboso ou sensível.
- Não duplicar o que o middleware já loga — handler loga ponto de decisão de negócio, não
  "iniciando"/"finalizando" genérico.

### 8. `// Arrange` / `// Act` / `// Assert` obrigatórios (do MT)

Diretriz de prioridade máxima no MT, num repo cuja regra geral é "evite comentários ao
máximo" — a exceção é deliberada. Custo zero, e o `conventions.md` da API já tem seção de
testes onde encaixa.

### 9. Nunca `.First()` / `.Single()` / `.Last()` sem `OrDefault` em dado externo (do MT)

Prefira `FirstOrDefault(...)` e trate o `null`/`default` explicitamente. O Vidro não tem
essa regra, e o webhook do Processor é exatamente "dado externo".

### 10. Fail-fast no domínio, anomalia + ACK no inbound (do MT) — melhor custo/benefício da lista

Regra do MT: em construtor de domínio, `throw` no overflow (fail-fast); em caminho de
consumer/inbound, **registre anomalia e ACK em vez de lançar** — não envenenar a fila.

Isto é literalmente o BUG-1 do `TODO.md`: `ArgumentException.ThrowIfNullOrWhiteSpace` no
construtor de `VideoArtifacts` + handler de webhook que estourava → 500 → Processor desiste
depois de 3 retries → job já ack'ado, não vai para o DLQ → vídeo preso em `Processing` para
sempre. O bug foi corrigido; a **regra que o preveniria de novo** não foi escrita em lugar
nenhum.

### 11. Tempo injetado, nunca lido dentro do método (do MT e do mamut-api)

O Vidro tem `DateTimeOffset now` como último parâmetro **de construtor de entidade**. O MT
estende para qualquer método que dependa de tempo (`now` como primeiro parâmetro), e o
mamut-api enforça o mesmo em Go com `forbidigo` proibindo `time.Now` e `uuid.New` fora da
borda. Vale unificar como regra dos três serviços, no `CLAUDE.md` da raiz.

### 12. Seção de anti-padrões nomeados (do MT)

O MT tem *"Anti-padrões (não copiar, mesmo aparecendo em repo bom)"* — cada item com o lugar
onde o padrão ruim aparece e por que não copiar. O Vidro documenta só o que é certo. Dado
que o `TODO.md` admite áreas bagunçadas, uma seção "isto existe no repo e não é para copiar"
evita que o agente replique o legado por imitação.

### 13. `.editorconfig` + `Directory.Build.props` — não existe nenhum

`find VidroApi -name "*.props" -o -name ".editorconfig"` volta vazio. O `conventions.md` da
API cita `// ReSharper disable once ...` em vários lugares, ou seja, os analisadores são
assumidos, mas nada está versionado nem roda no CI. É a mesma lacuna do golangci, do lado
.NET: `TreatWarningsAsErrors`, `EnableNETAnalyzers`, `AnalysisLevel` num
`Directory.Build.props` na raiz do `VidroApi/`.

### 14. Checklists como **skills**, não como parágrafo (do MT)

`msac/.claude/skills/` e `bank-slip/.claude/skills/` têm `create-http-feature`,
`create-consumer-feature`, `create-integration-test`, `add-test-scenario`, `publish-event`.
O Vidro tem os mesmos checklists — só que enterrados no fim de `conventions.md`, dependendo
de o agente lembrar de ler. Converter os três ("feature slice" da API, "pipeline step" do
Processor, "endpoint novo" do Front) em skills invocáveis é reempacotamento de conteúdo que
já existe.

---

## Frontend — VidroFront

Não há repo de referência; o que segue é sugestão, com dois furos concretos achados no
levantamento.

### ~~15. Nada roda `tsc --noEmit` — nem script, nem CI~~ ✅ *(2026-09-07)*

`tsconfig.json` já estava bem configurado (`strict`, `noUnusedLocals`, `noUnusedParameters`,
`noFallthroughCasesInSwitch`) e **nada executava o compilador**. Agora `bun run typecheck`
existe e o `.github/workflows/front.yml` roda o passo antes do test.

**A estimativa de "uma linha cada" estava errada:** ligar o compilador revelou **23 erros
pré-existentes** em 11 arquivos, corrigidos na mesma leva — inclusive um bug real
(`currentUser?.id` num `UserProfile` que só tem `userId`, deixando o `isOwner` do
`CommentList` sempre falso). Detalhe no `TODO.md`, em "Sujeira pequena". Lição para os itens
que ainda estão abertos aqui: **ligar um checador conta os erros que ele encontra** — o custo
real é a limpeza, não a configuração. Ver o item 16 (biome), que já sabia disso.

### 16. Lint desligado no CI por 108 erros pré-existentes

O comentário no `front.yml` é honesto sobre isso, e o `TODO.md` já registra. Caminho barato:
rodar `biome check --write` (limpa ~100), resolver o resto à mão numa leva, ligar o passo.
Enquanto não der, `biome ci --changed` já impede a dívida de crescer sem exigir a limpeza
completa primeiro.

### 17. Testes de geometria (do AAD) — melhor padrão de front dos quatro repos

`tests/smoke/layout.spec.ts` do AAD ataca uma classe que teste de conteúdo não pega:
**layout quebrado não falha um teste normal** — o elemento continua no DOM e continua
"visible" para o Playwright, só está fora da tela. Ele afirma geometria:
`scrollWidth <= clientWidth` em várias larguras, `elementFromPoint` para checar empilhamento
(`z-index` sob `position: sticky`), contagem de linhas via `range.getClientRects().length`.

Duas lições que valem mesmo sem copiar o arquivo:

- **Teste de largura precisa rodar no idioma de rótulo mais longo.** Medido no AAD: a mesma
  barra tem 358px em `en` e 438px em pt-BR. Esses 80px são a diferença entre um bug que só
  reproduz abaixo de 320px e um que reproduz a 414px, num celular real.
- **Teste de layout precisa de volume nas fixtures** (o AAD usa 24 torrents, 30 animes) —
  com fixture de duas linhas nada transborda e toda asserção passa vazia.

O Vidro tem player, grid de feed e playlists: mesmo risco. Hoje os 5 arquivos em
`VidroFront/src/tests/` cobrem só `api-client`, auth, users e videos. A estrutura em camadas
do AAD (`tests/unit/`, `tests/component/`, `tests/smoke/`) é o alvo.

### 18. `noUncheckedIndexedAccess: true`

Falta no `tsconfig.json`. Com `strict` ligado, é a maior fonte de `undefined` em runtime que
sobra — e o front consome listas paginadas da API o tempo todo.

---

## Ordem sugerida

1. **`.golangci.yml` no Processor** (item 1) — maior ganho, zero decisão de produto
2. **Regra do fail-fast vs. anomalia+ACK escrita** (item 10) — previne a recorrência do P0 já pago
3. ✅ ~~**`typecheck` no front**~~ (item 15) — feito em 2026-09-07 (custou 23 correções, não
   uma linha)
4. **Seção de logging no `conventions.md` da API** (item 7) — a única seção que falta comparada aos outros dois serviços
5. **Teste de contrato Go↔C# por AST** (item 3) — o que justifica o monorepo existir

O resto (itens 2, 4, 5, 6, 8, 9, 11, 12, 13, 14, 16, 17, 18) não tem dependência entre si e
pode entrar conforme se mexe na área correspondente.

---

## Já resolvido

- **2026-09-07** — item 15 (`typecheck` no front) fechado; ver a seção do item, reescrita com
  o custo real.

- **2026-08-31** — `VidroFront/docs/agents/conventions.md` dizia *"Forms: direct composition
  with shadcn components + `useState` + inline validation. No `react-hook-form` or form libs
  without discussion"*, enquanto os 8 formulários do repo usam `react-hook-form` +
  `zodResolver` + os componentes `Form*` do shadcn. Doc corrigida para descrever o padrão
  real. (Categoria "Documentação que mente" do `TODO.md`.)
