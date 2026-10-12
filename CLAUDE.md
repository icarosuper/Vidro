# CLAUDE.md — monorepo Vidro

Regras que valem para os **três serviços**. Cada serviço tem seu próprio `CLAUDE.md` com o que é
específico dele; o que está aqui não se repete lá.

## Os três serviços

| Serviço | O que é | Docs para agente |
|---|---|---|
| `VidroApi/` | API .NET (Clean + Vertical Slice), Postgres, MinIO, Redis | `VidroApi/docs/agents/` |
| `VidroFront/` | TanStack Start + React, consome a API | `VidroFront/docs/agents/` |
| `VidroProcessor/` | Worker Go: fila Redis → pipeline FFmpeg → MinIO → webhook | `VidroProcessor/docs/agents/` |

Os três são **um repositório só** — este. A raiz carrega o `docker-compose.yml` do stack inteiro,
o `TODO.md`, este arquivo e o `docs/MONOREPO.md` (por que é monorepo, e o que isso custou).

**Escopo de edição:** o padrão continua sendo mexer em **um** serviço por tarefa — leia os outros
dois à vontade para entender contratos, tipos e endpoints. A exceção é o contrato compartilhado:
aí os dois lados mudam juntos, no mesmo commit, e isso é o comportamento certo, não invasão de
escopo. Fora esse caso, faltou algo no outro serviço — informe o usuário antes de mexer.

**Shared contract** (Redis queue names, the job envelope, MinIO paths, the webhook shape): change
both sides **in the same commit**. The `video-processed` webhook payload and the job the API
publishes (queue name, `job:<id>` key, envelope fields) are locked by test: the goldens in
[`contracts/`](contracts/README.md) are read by the worker and by the API, so changing a field
means editing the golden and both sides together. Only the MinIO path layout is still discipline.
It is the main reason this is a monorepo — both P0s in `TODO.md` were contract divergences nobody
saw for months. See `VidroProcessor/docs/agents/design-decisions.md`.

## Language: English only

**Everything in English**, in all three services and at the root: code (class, method, variable,
type, test, log message, comment, XML doc), docs, file and directory names, commit messages, PR
titles. No exceptions. (The UI was already English — `<html lang="en">`.)

Some older text is still in Portuguese — most of the root docs, `TODO.md`, and the `VidroFront`
docs. It gets translated **on contact**, not all at once: whenever you edit or pass through a file
and find Portuguese in it, translate it to English as part of the task and **tell the user** what you
translated. This is an exception to "Código ruim no caminho" below — no need to ask first, just report it.
Renaming a file means fixing every link to it in the same change (`grep` for the old name).

Not translated: test data whose point is the content itself (e.g. `"joão"` testing accents).

## Legibilidade

Valem para os **três** serviços — C#, TypeScript e Go. O que é idiomático de uma linguagem fica no
`conventions.md` do serviço; o que está aqui não se repete lá.

- **Variável nomeada em vez de expressão inline.** O resultado de uma checagem, query ou chamada
  async recebe um nome antes de entrar numa condição. Nunca inline dentro do `if`.

  ```ts
  // ✅
  const emailAlreadyTaken = users.some((u) => u.email === email)
  if (emailAlreadyTaken) return { error: 'Email already in use' }

  // ❌
  if (users.some((u) => u.email === email)) return { error: 'Email already in use' }
  ```

- **Lógica complexa vira função com nome.** Se o bloco precisa de um comentário para se explicar,
  ele precisa de um nome.

- **Nome diz "o quê", não "como".** Sem abreviação, sem nome de uma letra (fora índice de loop),
  sem genérico — `result`, `data`, `temp`.

- **Ternário curto cabe em uma linha; longo ou aninhado vai para três** — condição, `?`, `:`.
  A regra até 2026-09-07 era "sempre três linhas, nunca uma"; ela caiu porque o formatter do
  Biome (`VidroFront`) colapsa ternário curto e não tem opção para preservar — manter a regra
  custava desligar o formatter do front inteiro. No front **quem decide é o formatter**: rode
  `bun run lint`. Em C# e Go, use o julgamento — o critério é o mesmo que o formatter aplica,
  quebrar quando não couber na linha.

  ```ts
  // ✅ curto
  const label = isAuthenticated ? 'Sign out' : 'Sign in'

  // ✅ longo ou aninhado — três linhas, e considere extrair para uma função nomeada
  const nextMode = mode === 'light'
    ? 'dark'
    : resolveModeFromSystemPreference(mode)
  ```

## Vídeo preso em `Processing`

O runbook é [`docs/troubleshooting-stuck-video.md`](docs/troubleshooting-stuck-video.md). Mora na
raiz porque atravessa API → Redis → worker → MinIO; nenhum serviço sozinho o resolve.

## Commit message convention

Conventional Commits, **in English**, subject line only — no body, no scope, no footer (no
`Co-authored-by`).

Format: `<type>: <imperative verb> <complement>` — lowercase after the type, no trailing period,
up to ~72 chars.

Types used in the repo (by real frequency): `feat` > `chore` > `fix` > `refactor` > `docs` / `test`.

- `feat` — new or extended functionality
- `fix` — bug/behavior fix
- `chore` — docs, README, migrations, scaffold, reorganization without logic
- `refactor` — rename/restructure without changing behavior
- `docs` / `test` — when the change is only documentation or only tests

Examples: `feat: add channel avatar upload`, `fix: fix reactions button`, `chore: update README`,
`refactor: rename project`. (Commits before 2026-10-12 are in Portuguese; history is not rewritten.)

The subject states the **behavior**, not the file: describe the rule that now holds. If the diff
doesn't fit in one subject, it has become more than one commit.

Título de PR (squash merge): `Feature/nome-da-branch (#N)`.

## Onde commitar

- **Padrão: direto na `master`.** Coisa pequena e bugfix não abre branch.
- **Exceção: feature grande** (vários commits). Aí **pergunte ao usuário** se é para criar
  `feature/<topic>` ou mandar direto para `master` — nunca decida sozinho.
- `master` sempre deployável; produção sai de tags `vX.Y.Z` (estratégia pretendida — ainda não há
  tag nenhuma). Uma tag versiona **os três serviços de uma vez**: é o preço, e a vantagem, do
  monorepo — nunca existe combinação de versões que não foi testada junta. Ver `docs/MONOREPO.md`.
- **Não existe pipeline de deploy.** `.github/workflows/{api,front,processor}.yml` só buildam e
  testam, um por serviço, com filtro de path. Deploy é manual.

## Nunca commitar sem pedido explícito

1. Implementar as mudanças
2. Rodar os testes e verificar que passam
3. Mostrar o que mudou e **sugerir um** título de commit (não uma lista de opções)
4. Esperar o usuário aprovar ou pedir o commit

## Código ruim no caminho

Achou código ruim **perto** do que você está mexendo — mal escrito, deprecado, não utilizado,
desatualizado, não otimizado, duplicado, comentário mentindo sobre o código — **pergunte antes de
agir**. Sempre as três opções, e o usuário escolhe:

1. **aproveitar e ajustar agora**
2. **só deixar um TODO** (no código, junto do trecho)
3. **ignorar**

Nunca decida sozinho por nenhuma das três — nem "arrumo de passagem porque é rápido", nem "deixo
quieto porque está fora do escopo". Diga o que é, onde está (`arquivo:linha`) e por que é ruim, em
uma ou duas linhas, e pergunte. Vale para o que você encontra de passagem; o que o pedido do usuário
já cobre não é "de passagem", é o trabalho.

## Depois de qualquer mudança

1. **Rodar os testes** do serviço que você tocou (`dotnet test` / `bun run test` / `go test ./...`).
2. **Atualizar as docs afetadas na mesma leva** — o gatilho de cada arquivo está no `CLAUDE.md` do
   serviço. Doc desatualizada custa mais que código faltando: o `TODO.md` desta raiz tem uma seção
   inteira ("Documentação que mente") que só existiu por isso.
   As docs da **raiz** também têm gatilho:
   - **`TODO.md`** — a task fecha, avança ou descobre um item: riscar/atualizar o item no corpo
     **e** o resumo "Fechado até agora" do topo **e** a "Ordem sugerida" do fim. Os três juntos —
     riscar só o corpo foi o que deixou a ordem sugerida apontando para trabalho já feito.
   - **`docs/observabilidade.md`, `docs/padroes-a-importar.md`, `docs/MONOREPO.md`** — fechou um
     furo, degrau ou item que eles listam: marcar ✅ lá também, inclusive na "Ordem sugerida" e
     na seção "O que já existe" quando houver.
   - **`README.md`** — mudou porta, comando, fluxo entre serviços ou arquivo de doc da raiz.
   - Antes de sugerir o commit, `grep` pelo nome do item/arquivo nas docs da raiz e do serviço:
     doc que ainda o chama de "falta" ou "próximo" está mentindo.
3. **Sugerir o título do commit** e parar.
