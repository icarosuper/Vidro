# Planos por fase — VidroFront

O front foi construído fase a fase, cada uma entregando algo funcional. **As nove estão
concluídas**; esta lista é o registro do que cada fase entregou — só quatro delas geraram plano
escrito (os arquivos ao lado).

1. ✅ **Scaffold** — TanStack Start + shadcn + estrutura + api-client — [plano](2026-04-07-fase1-scaffold.md)
2. ✅ **Auth** — signIn, signUp, signOut, renovação de token, proteção de rota — [plano](2026-04-08-fase2-auth.md)
3. ✅ **Settings** — perfil do usuário, avatar — [plano](2026-04-08-fase3-settings.md)
4. ✅ **Channel** — create/edit/delete
5. ✅ **Home + Watch** — trending, feed, HLS player, reações
6. ✅ **Public channel/user pages** — SSR (SEO ainda não existe: só `__root.tsx` define `head`)
7. ✅ **Upload** — presigned URL, progresso, polling de status
8. ✅ **Comments** — list, add, reply, edit, delete, reactions
9. ✅ **Playlists** — CRUD, itens, página pública — [plano](2026-04-10-fase9-playlists.md)

Desenho geral do frontend: [2026-04-07-frontend-design.md](2026-04-07-frontend-design.md).

O ciclo pós-implementação (testes → docs → commit sugerido) mora no `CLAUDE.md` da raiz do
monorepo, seção "Depois de qualquer mudança".
