# Frontend Design — Vidro

**Data:** 2026-04-07  
**Stack:** TanStack Start, TanStack Query, shadcn/ui, Tailwind CSS, hls.js, react-hook-form + zod  
**Diretório:** `FrontNovo/`

---

## Contexto

Reescrita frontend POC (Svelte SPA em `Front/`) com stack production-ready. API .NET completa + documentada. Novo front construído incrementalmente, fase por fase.

---

## Estrutura de Pastas

```
FrontNovo/
├── app/
│   ├── routes/
│   │   ├── __root.tsx              # layout raiz (nav, providers)
│   │   ├── index.tsx               # / home (SSR)
│   │   ├── watch.$videoId.tsx      # /watch/:videoId (SSR)
│   │   ├── search.tsx              # /search (SSR)
│   │   ├── $username.tsx           # /:username — página pública do usuário (SSR)
│   │   ├── $username.$channel.tsx  # /:username/:channelHandle — canal (SSR)
│   │   ├── upload.tsx              # /upload (client-only, auth required)
│   │   ├── settings.tsx            # /settings (client-only, auth required)
│   │   └── dashboard.tsx           # /dashboard (client-only, auth required)
│   ├── features/
│   │   ├── auth/
│   │   │   ├── components/         # SignInForm, SignUpForm
│   │   │   ├── hooks.ts            # useAuth, useCurrentUser
│   │   │   ├── server.ts           # server functions: signIn, signOut, renewToken
│   │   │   └── types.ts
│   │   ├── videos/
│   │   │   ├── components/         # VideoCard, VideoPlayer, VideoGrid
│   │   │   ├── hooks.ts            # useVideo, useTrending, useFeed, useSearch
│   │   │   ├── api.ts
│   │   │   └── types.ts
│   │   ├── channels/
│   │   ├── comments/
│   │   ├── playlists/
│   │   └── users/
│   ├── shared/
│   │   ├── components/             # wrappers shadcn (Button, Avatar, etc)
│   │   ├── lib/
│   │   │   ├── api-client.ts       # fetch wrapper central
│   │   │   └── utils.ts
│   │   └── types.ts                # EnumValue, ApiResponse, erros
│   └── router.tsx
├── public/
├── package.json
└── app.config.ts
```

---

## Estratégia de Renderização

| Rota | Estratégia | Motivo |
|------|-----------|--------|
| `/` | SSR | Feed personalizado se logado, trending se não |
| `/watch/:videoId` | SSR | Metatags OG — crítico SEO + compartilhamento |
| `/search` | SSR | Query param indexável (`?q=`) |
| `/:username` | SSR + ISR | Pág. pública, dados mudam pouco |
| `/:username/:channelHandle` | SSR + ISR | Idem |
| `/upload` | Client-only | Auth obrigatória, sem SEO |
| `/dashboard` | Client-only | Idem |
| `/settings` | Client-only | Idem |

Proteção rotas autenticadas via `beforeLoad` no root rotas privadas — redireciona `/` c/ modal login se sem sessão.

---

## Auth Flow

```
Browser → server function (signIn) → POST /v1/auth/signin → API
                                   ← { accessToken, refreshToken, secondsToExpiration }

server function:
  - seta refreshToken em httpOnly cookie
  - retorna accessToken ao client

client:
  - guarda accessToken em memória (React context/atom)
```

**Renovação automática (interceptor no api-client):**
1. api-client detecta 401
2. Chama server function `renewToken`
3. Server function lê cookie httpOnly → POST `/v1/auth/renew-token` → novos tokens
4. Atualiza cookie → retorna novo accessToken
5. api-client repete request original
6. Se falhar → limpa sessão → redireciona login

**SSR:** rotas SSR leem cookie httpOnly via server function → accessToken válido antes fetches — sem waterfall no cliente.

**Segurança:** refreshToken nunca toca JS do browser. Exclusivo no cookie httpOnly + servidor — imune a XSS.

---

## API Client

```ts
// shared/lib/api-client.ts
// - baseURL por env
// - injeção automática do accessToken (memória)
// - interceptor 401: renova token, repete request
// - erros mapeados para ApiError tipado
// - AbortSignal para cancelation
```

**Tipos base:**
```ts
type ApiSuccess<T> = { data: T }
type ApiError = { code: string; message: string }
type ValidationError = { errors: { field: string; message: string }[] }
type EnumValue = { id: number; value: string }
type CursorPage<K extends string, T> = { [key in K]: T[] } & { nextCursor: string | null }
```

Cada `features/*/api.ts` importa client, exporta funções tipadas. Nenhuma feature faz `fetch` direto. TanStack Query nos hooks, consome funções do `api.ts`. Query keys: `['videos', videoId]`, `['channels', handle]`, etc.

---

## Ordem de Implementação

| Fase | O que entra | Resultado |
|------|-------------|-----------|
| 1 | Scaffold (TanStack Start + shadcn + Tailwind + estrutura + api-client + tipos base) | Projeto rodando |
| 2 | Auth (signIn, signUp, signOut, renovação, proteção de rotas) | Login/logout funcionando |
| 3 | Settings (perfil do usuário, avatar) | ✅ Usuário editável para testes |
| 4 | Canal (criar, editar, deletar, avatar, dashboard básico) | Canal editável para testes |
| 5 | Home + Watch (trending, feed, player HLS, registro de view, reações) | ✅ Núcleo do produto funcionando |
| 6 | Canal + Usuário públicos (`/:username`, `/:username/:handle`) | SSR + SEO completo |
| 7 | Upload (presigned URL, progress, polling de status) | Criação de conteúdo |
| 8 | Comentários (listagem, add, reply, edit, delete, reações) | Engajamento |
| 9 | Playlists (criação, gerenciamento, add/remove vídeos) | Feature completa |

Cada fase termina com produto funcionando — nada pela metade.

---

## Notas de Implementação

- **URLs amigáveis:** canais por `username + channelHandle`. Endpoints públicos `/users/{username}/channels/{handle}`, autenticados `/channels/{handle}`
- **Upload:** 2 etapas via presigned PUT — arquivo direto ao MinIO, nunca passa pela API
- **EnumValue:** enum na resposta = `{ id: number; value: string }`. No request, enviar inteiro
- **Paginação:** cursor = `DateTimeOffset` ISO 8601. Playlists usam `PagedResult<T>` com `items` + `nextCursor`
- **Thumbnails/URLs:** presigned MinIO (expiram) — não cachear longo prazo
- **Comentários deletados:** `isDeleted: true`, `content: null` — manter na UI p/ preservar threads