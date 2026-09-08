# Monorepo — por que, como, e o que custou

Registro da migração de três repositórios (`VidroApi`, `VidroFront`, `VidroProcessor`) para um só,
feita em **30/ago/2026**. Leia antes de propor voltar atrás, adicionar ferramenta de monorepo, ou
mexer no CI.

Os caminhos citados aqui são relativos à **raiz do monorepo**, não a esta pasta.

---

## Descobertas que motivaram a mudança

Nenhuma delas é opinião de arquitetura — todas apareceram no próprio repositório.

1. **Três arquivos compartilhados moravam fora de qualquer repo.** `docker-compose.yml` (o que faz o
   stack existir), `TODO.md` e `CLAUDE.md` viviam soltos na pasta que continha os três clones, sem
   versionamento nenhum. O monorepo já estava nascendo; faltava o `git init`.

2. **As docs já se referenciavam entre serviços por caminho relativo.** `VidroFront/CLAUDE.md` manda
   ler `../VidroApi/docs/agents/features-index.md`; a API aponta para
   `../../../VidroProcessor/docs/agents/troubleshooting-stuck-video.md`. Esses links só funcionavam
   porque os três clones estavam lado a lado com esses nomes exatos — uma suposição de monorepo sem
   a garantia de um.

3. **Os P0 do `TODO.md` são todos divergência de contrato entre serviços**, e o próprio texto diz
   "achados nesta análise, **não registrados em lugar nenhum**". BUG-1: o Processor documentou que o
   webhook de sucesso pode vir sem artefato opcional, a API não cumpriu, e todo vídeo nessa condição
   ficava preso em `Processing` para sempre. Em repos separados isso é achado arqueológico; num
   commit atômico é CI vermelho.

4. **A convenção já mandava tratá-los como um só.** "Quando mudar contrato compartilhado, os dois
   repos precisam ser tagueados e deployados juntos" estava escrito em dois `CLAUDE.md`. Era
   disciplina manual para um problema que a estrutura resolve.

5. **Um dev só.** Nenhum dos motivos reais para repos separados se aplica: não há permissão por
   time, cadência de release independente, nem dono diferente por serviço.

---

## Decisões

### 1. Monorepo, sem ferramenta de monorepo

Nada de Turborepo, Nx, moon ou Bazel. **Turborepo é ferramenta do ecossistema JS** e orquestra
scripts de `package.json` sobre workspaces — aqui existe **um** pacote JS em três serviços. Para os
outros dois só entraria via `package.json` de fachada chamando `dotnet build` / `go build`.

Os três ganhos do Turborepo não existem neste projeto:

- **Grafo de dependência entre pacotes** — não há. O front não importa a API em build time; conversa
  por HTTP em runtime. Não existe ordem topológica a resolver.
- **Cache de tarefas** — cada toolchain já tem o seu, e melhor: build incremental do `dotnet`, build
  cache do Go, Vite/bun no front. O Turbo cachearia por cima deles.
- **Remote cache de time** — um dev só.

As poliglotas de verdade (Nx, moon, Bazel) cobram configuração adiantada que só se paga com dezenas
de pacotes, times paralelos ou build que dói. Três serviços com build rápido não chegam perto.

**Gatilho para reavaliar:** quando existir um **segundo** pacote JS — um `packages/api-types` gerado
do OpenAPI, consumido pelo front e por um eventual admin. Mesmo aí a resposta primeiro é **bun
workspaces**, que já está instalado e é de graça. Turborepo só se, depois disso, o build incomodar.

### 2. `git subtree add`, não `filter-repo`

Histórico preservado com git puro, sem ferramenta externa:

```bash
git subtree add --prefix=VidroApi ../vidro-legacy/VidroApi master
```

Os 121 commits das três histórias estão no repositório e `git blame` atribui cada linha ao commit
original (inclusive através do rename histórico `VidaroApi` → `VidroApi`).

A alternativa, `git filter-repo --to-subdirectory-filter`, reescreveria os caminhos históricos e
deixaria `git log -- VidroApi/` perfeito, ao custo de reescrever todos os SHAs e depender de uma
ferramenta fora do git. Ver o tradeoff #1 abaixo.

### 3. CI: um workflow por serviço, com filtro de path nativo

`.github/workflows/{api,front,processor}.yml`, cada um com `on.push.paths` / `on.pull_request.paths`
apontando para o seu diretório e para si mesmo. Commit no front não dispara build de .NET.

**Por que não um workflow só com `if:` por job:** o `on:` é do workflow, não do job, então um arquivo
único exigiria uma action de terceiro (`dorny/paths-filter`) para decidir o que rodar. Três arquivos
com filtro nativo entregam o mesmo sem dependência.

Os três `.github/workflows/ci.yml` antigos foram apagados: o GitHub só lê `.github/workflows/` da
raiz, então dentro dos subdiretórios eram arquivos mortos apontando para um mundo que não existe
mais.

### 4. Os clones antigos viraram backup, não lixo

`../vidro-legacy/{VidroApi,VidroFront,VidroProcessor}` são os três repositórios originais, intactos,
com seus `origin` no GitHub. São a fonte do subtree e o caminho de rollback. **Não apague antes de
confirmar que o monorepo está no GitHub e que ninguém precisa dos remotes antigos.**

---

## Tradeoffs aceitos

### 1. `git log -- <serviço>/` só enxerga a história pós-migração

Consequência direta do `subtree add`: commits antigos carregam caminhos sem o prefixo
(`src/VidroApi.Api/Program.cs`, não `VidroApi/src/...`), e `--follow` não atravessa o merge do
subtree.

O que **continua funcionando**: `git log` completo, `git show <sha>`, e principalmente `git blame`,
que atribui as linhas aos commits originais. Na prática o que se perde é o log filtrado por
diretório para commits pré-migração.

Saída, se algum dia incomodar: refazer a importação com
`git filter-repo --to-subdirectory-filter <serviço>` em cada clone antes de mesclar. Reescreve SHAs,
então só vale antes de o monorepo ter história própria relevante.

### 2. Uma tag versiona os três serviços

`v1.2.0` passa a cobrir API, front e worker juntos — o worker ganha versão quando só o front mudou.

Aceito porque os três sobem no mesmo `docker-compose.yml` e a convenção já exigia deploy coordenado
em mudança de contrato. O ganho é maior que o ruído: **nunca existe combinação de versões que não foi
testada junta.**

Quem precisar de versionamento independente depois: tags com prefixo (`api/v1.2.0`) resolvem sem
sair do monorepo.

### 3. Checkout maior

Um clone traz .NET + Go + TypeScript. Irrelevante nesta escala; vira problema quando o histórico
pesa, e aí a resposta é `git clone --filter=blob:none`, não voltar a três repos.

### 4. Filtro de path × branch protection

Job pulado por filtro de path aparece como "skipped", não "success". Se algum dia houver branch
protection exigindo esses checks, um PR que só mexe no front ficaria travado esperando o check da API
que nunca vai rodar. Hoje não há branch protection (commits vão direto para a `master`), então não
morde. Se for ligar: use um job `all-green` que sempre roda e depende dos outros com
`if: always()`.

---

## O que a migração destrava (e ainda não foi feito)

O maior ganho não é build, é **contrato**. Duas coisas que hoje são manuais e já custaram bug:

- **API ↔ Front — tipos gerados.** O `types.ts` do front espelha as shapes do backend **na mão**,
  enums inclusive (`VideoStatus`, `VideoVisibility`, `ReactionType`). O P0.1 do `TODO.md` ("docs que
  mentem", 4 rotas erradas no `features-index.md`) é esse problema se manifestando. A API já tem
  `MapOpenApi()` no `Program.cs` — hoje só em Development, o que basta para gerar tipos em dev/CI com
  `openapi-typescript` e falhar o CI quando front e API divergirem.
- **API ↔ Processor — teste de contrato.** ✅ *(2026-09-07, parcial)* O payload do webhook
  `video-processed` virou golden versionado em [`contracts/`](../contracts/README.md), testado dos
  dois lados: o worker prova que serializa exatamente aquilo, a API prova que aceita exatamente
  aquilo. É a rede que pegaria o BUG-1 no ato. **Falta o resto do contrato** — nome da fila
  (`JobQueueSettings:QueueName` ↔ `PROCESSING_REQUEST_QUEUE`), `callback_url` no `JobState` e o
  layout de paths no MinIO continuam só documentados.

Nenhum dos dois precisa de ferramenta de monorepo. Precisavam do monorepo.
