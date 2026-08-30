# CLAUDE.md

Guia nav Claude Code repo. Regras críticas aqui; resto em `docs/agents/`.

Convenção de commit, branching, escopo de edição e a regra de "código ruim no caminho" moram **uma
vez só**, no `../CLAUDE.md` da raiz do workspace (carregado junto com este arquivo). Resumo:
conventional commits **em português**, só o assunto; direto na `master` salvo aprovação do usuário;
**nunca commite sem pedido explícito**.

## Projeto

Reescrita frontend Vidro (plataforma vídeo). Backend .NET `../VidroApi/` completo. Frontend construído incremental, fase a fase.

## Regras sempre ativas

- **Idioma:** todo código inglês (vars, funções, tipos, comentários, testes). **Commits português.**
- **Escopo:** nunca edite arquivos fora dir. Pode **ler** `../VidroApi/` p/ entender contratos, não modificar. Endpoint faltando algo → informe usuário.
- **Endpoints da API:** antes implementar endpoint frontend, **leia `../VidroApi/docs/agents/features-index.md`** p/ confirmar path exato, arquivo fonte backend (`src/VidroApi.Api/Features/...`) e shape request/response. Não adivinhe paths.
- **Sem `fetch` direto:** todo HTTP via `apiClient` (`src/shared/lib/api-client.ts`). Exceções: uploads presigned e `features/*/server.ts`.

## Comandos

```bash
bun run dev                                 # http://localhost:3000
bun run build
bun run test                                # todos
bun run test src/tests/api-client.test.ts   # um arquivo
biome check
biome format
```

## Stack resumida

TanStack Start (SSR seletivo, file-based routing) · TanStack Query · shadcn/ui + Tailwind v4 · bun · Biome · Vitest

`VITE_API_URL` → backend .NET (default `http://localhost:5000`)

## Onde ler o quê

Leia doc **antes** tarefa descrita. Pular → infringir padrões que existem por motivo.

- **[docs/agents/architecture.md](docs/agents/architecture.md)** — Antes criar rotas, mexer camadas, `apiClient`, tipos compartilhados ou providers root. Cobre layout `src/`, responsabilidades, regra API client, estratégia renderização, formatos resposta API, query keys.

- **[docs/agents/conventions.md](docs/agents/conventions.md)** — Antes escrever qualquer código: legibilidade (vars nomeadas, ternários 3 linhas, extração funções), naming, layout feature module, padrões `api.ts`/`hooks.ts`, error handling, imports. Traz também o **checklist de endpoint novo** e o de **feature module nova** — siga o checklist em vez de copiar um vizinho de olho.

- **[docs/agents/auth.md](docs/agents/auth.md)** — Antes mexer sign in/up/out, proteção rota, `tokenStore`, `renewToken`, ou qualquer coisa SSR hydration. Cobre regra ordem sign out e padrão server snapshot em `useIsAuthenticated`.

- **[docs/agents/features-index.md](docs/agents/features-index.md)** — Antes criar/mexer feature, ou buscar "onde está endpoint X / hook Y". Mapa cada feature: arquivos, endpoints backend, hooks, tipos, componentes. **Atualize ao add/remover endpoints, hooks ou componentes.**

- **[docs/agents/workflow.md](docs/agents/workflow.md)** — Antes nova fase, ou fim de cada passo implementação. Cobre fases projeto e ciclo pós-implementação (testes → docs → commit sugerido → próximos passos).

- **[docs/plans/](docs/plans/)** — Planos detalhados por fase. Ver plano fase ativa antes implementar; marcar tarefas ✅ conforme concluídas.

## Vídeo preso em `Processing`

Não é bug do front. O runbook cruza API e worker: `../VidroProcessor/docs/agents/troubleshooting-stuck-video.md`.
