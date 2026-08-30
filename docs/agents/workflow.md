# Workflow

Leia doc: entender projeto fase por fase e o ciclo pós-implementação.

Branching, convenção de commit e "nunca commite sem pedido explícito" estão no `../../../CLAUDE.md`
da raiz do workspace — não se repetem aqui.

## Escopo de edição

Pode **ler** outros projetos (ex: `../VidroApi/`, `../VidroProcessor/`) p/ entender contratos, tipos, endpoints. **Nunca edite** fora deste repo (`VidroFront/`). API faltar dados → **informe usuário**, não altere backend.

## Fases de implementação

Planos detalhados em `docs/plans/`:

1. ✅ **Scaffold** — TanStack Start + shadcn + estrutura + api-client
2. ✅ **Auth** — signIn, signUp, signOut, renovação de token, proteção de rota
3. ✅ **Settings** — perfil do usuário, avatar
4. ✅ **Channel** — create/edit/delete
5. ✅ **Home + Watch** — trending, feed, HLS player, reações
6. ✅ **Public channel/user pages** — SSR (SEO ainda não existe: só `__root.tsx` define `head`)
7. ✅ **Upload** — presigned URL, progresso, polling de status
8. ✅ **Comments** — list, add, reply, edit, delete, reactions
9. ✅ **Playlists** — CRUD, itens, página pública

**Cada fase: entrega funcional.** Nada meio feito entre fases.

## Working style — após cada passo

1. **Rodar testes.** `bun run test` pós-feature. Corrigir falhas antes de prosseguir.
2. **Atualizar docs.** Reflita mudanças em:
   - `docs/plans/` — marcar ✅
   - `docs/agents/features-index.md` — ao add/remover endpoints, hooks, componentes
   - `docs/agents/architecture.md` — ao mudar camadas, api-client, renderização
   - `docs/agents/auth.md` — qualquer mudança de auth
   - `docs/agents/conventions.md` — só se convenção mudar (raro)
3. **Sugerir commit message em português.** Usuário revisa e commita.
4. **Mostrar próximos passos.** Lista breve p/ usuário escolher.

## Comandos frequentes

```bash
bun run dev                                 # http://localhost:3000
bun run build                               # build de produção
bun run test                                # todos os testes
bun run test src/tests/api-client.test.ts   # um arquivo específico
biome check                                 # lint
biome format                                # format
```