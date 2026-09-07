# CLAUDE.md

Guia nav Claude Code repo. Regras críticas aqui; resto em `docs/agents/`.

Convenção de commit, branching, escopo de edição, idioma do código, release/tags e a regra de
"código ruim no caminho" moram **uma vez só**, no `../CLAUDE.md` da raiz do monorepo (carregado
junto com este arquivo). Resumo: código em inglês, conventional commits **em português**, só o
assunto; direto na `master` salvo aprovação do usuário; **nunca commite sem pedido explícito**.

## Projeto

Reescrita frontend Vidro (plataforma vídeo). Backend .NET `../VidroApi/` completo. Frontend construído incremental, fase a fase.

## Regras sempre ativas

- **Endpoints da API:** antes implementar endpoint frontend, **leia `../VidroApi/docs/agents/features-index.md`** p/ confirmar path exato, arquivo fonte backend (`src/VidroApi.Api/Features/...`) e shape request/response. Não adivinhe paths.
- **Sem `fetch` direto:** todo HTTP via `apiClient` (`src/shared/lib/api-client.ts`). Exceções: uploads presigned e `features/*/server.ts`.

## Comandos

```bash
bun run dev                                 # http://localhost:3000
bun run build
bun run typecheck                           # tsc --noEmit (roda no CI)
bun run test                                # todos
bun run test src/tests/api-client.test.ts   # um arquivo
bun run lint                                # biome ci (roda no CI)
biome check                                 # o mesmo, com saída para iterar local
```

**O formatter manda na formatação** — `bun run lint` (`biome ci`) roda no CI e falha com
arquivo fora do padrão (2 espaços, aspas simples, sem ponto e vírgula). Ele colapsa ternário
curto em uma linha, e a regra da raiz foi ajustada para permitir isso; não tente preservar
ternário de três linhas curto, o formatter desfaz.

Três armadilhas do Biome que custam tempo:

- Comentário `//` dentro do `biome.json` **quebra a config em silêncio** — cai no default e
  passa a lintar `dist/` (83 → 148 arquivos), sem erro nenhum.
- `biome-ignore` só funciona com o motivo em **uma linha**; quebrado em duas vira "unused
  suppression" e a regra continua acusando.
- A suppression vale só para a **linha seguinte**. Em JSX multi-linha ela vai **dentro da
  tag**, colada no atributo (`key={i}`) — se ficar antes do `<div`, o formatter reposiciona o
  atributo e a suppression descola.

## Stack resumida

TanStack Start (SSR seletivo, file-based routing) · TanStack Query · shadcn/ui + Tailwind v4 · bun · Biome · Vitest

`VITE_API_URL` → backend .NET (default `http://localhost:5000`)

## Onde ler o quê

Leia doc **antes** tarefa descrita. Pular → infringir padrões que existem por motivo.

- **[docs/agents/architecture.md](docs/agents/architecture.md)** — Antes criar rotas, mexer camadas, `apiClient`, tipos compartilhados ou providers root. Cobre layout `src/`, responsabilidades, regra API client, estratégia renderização, formatos resposta API, query keys.

- **[docs/agents/conventions.md](docs/agents/conventions.md)** — Antes escrever qualquer código: naming, layout feature module, padrões `api.ts`/`hooks.ts`, error handling, imports. (Legibilidade — vars nomeadas, ternários em 3 linhas, extração de função — está no `../CLAUDE.md` da raiz e vale para os três serviços.) Traz também o **checklist de endpoint novo** e o de **feature module nova** — siga o checklist em vez de copiar um vizinho de olho.

- **[docs/agents/auth.md](docs/agents/auth.md)** — Antes mexer sign in/up/out, proteção rota, `tokenStore`, `renewToken`, ou qualquer coisa SSR hydration. Cobre regra ordem sign out e padrão server snapshot em `useIsAuthenticated`.

- **[docs/agents/features-index.md](docs/agents/features-index.md)** — Antes criar/mexer feature, ou buscar "onde está endpoint X / hook Y". Mapa cada feature: arquivos, endpoints backend, hooks, tipos, componentes. **Atualize ao add/remover endpoints, hooks ou componentes.**

- **[docs/plans/README.md](docs/plans/README.md)** — Índice das 9 fases (todas concluídas) e o que cada uma entregou. Os planos detalhados estão ao lado; marcar tarefas ✅ conforme concluídas.

## Manter as docs em dia

O ciclo pós-implementação (testes → docs → commit sugerido) está no `../CLAUDE.md` da raiz. O que é
específico daqui é **qual** arquivo atualizar:

- Add/remover endpoint, hook ou componente → `docs/agents/features-index.md`
- Mudar camadas, `apiClient` ou estratégia de renderização → `docs/agents/architecture.md`
- Qualquer mudança de auth → `docs/agents/auth.md`
- Mudou uma convenção de código (raro) → `docs/agents/conventions.md`
- Nova fase concluída → `docs/plans/README.md`
