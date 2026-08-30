# Architecture

Leia doc antes criar rotas, features novas ou mudanças transversais (api-client, tipos compartilhados, providers no root).

## Stack

- **Framework:** TanStack Start (SSR seletivo, file-based routing)
- **Data fetching:** TanStack Query
- **UI:** shadcn/ui + Tailwind CSS v4
- **Forms:** componentes próprios + validação inline (sem react-hook-form)
- **Notificações:** sonner (`toastApiError` centraliza erros da API)
- **Package manager:** bun
- **Lint/format:** Biome
- **Testes:** Vitest

## Layout de `src/`

```
src/
├── routes/                # File-based routes (TanStack Router)
│   ├── __root.tsx         # Layout root: Header, providers, auth hydration, devtools
│   ├── index.tsx          # Home (trending + feed)
│   ├── watch.$videoId.tsx # Player + reações
│   ├── search.tsx
│   ├── $username.tsx
│   ├── $username.index.tsx
│   ├── $username.$channel.tsx
│   ├── upload.tsx         # Client-only, auth required
│   ├── settings.tsx       # Client-only, auth required
│   └── dashboard.tsx      # Client-only, auth required
├── features/              # Vertical slice — um módulo por domínio
│   ├── auth/              # components/, hooks.tsx, server.ts, types.ts
│   ├── users/             # components/, hooks.ts, api.ts, types.ts
│   ├── channels/
│   ├── videos/            # inclui server.ts p/ SSR do /watch
│   ├── comments/
│   └── playlists/
├── shared/
│   ├── components/ui/     # shadcn/ui wrappers
│   ├── lib/
│   │   ├── api-client.ts  # fetch wrapper central + renewToken
│   │   ├── token-store.ts # access token em memória (client-only)
│   │   ├── error-messages.ts
│   │   └── toast-error.ts
│   └── types.ts           # ApiSuccess/ApiError/EnumValue/CursorPage/PagedResult + enums
├── components/            # Header, ThemeToggle, shadcn/ui/*
├── integrations/
│   └── tanstack-query/    # QueryClient + devtools
├── lib/utils.ts
└── tests/
```

## Camadas e responsabilidades

| Camada | Papel | Importa de |
|---|---|---|
| `routes/*` | File-based routing, loaders, `beforeLoad` (auth guard), composição componentes feature | `features/*`, `shared/*`, `components/*` |
| `features/*/api.ts` | Funções tipadas → `apiClient`. Nunca `fetch` direto (exceto uploads presigned) | `shared/lib/api-client`, `./types` |
| `features/*/hooks.ts(x)` | Hooks TanStack Query (`useQuery`/`useMutation`/`useInfiniteQuery`) + invalidações | `./api`, `shared/lib/toast-error` |
| `features/*/server.ts` | Server functions (`createServerFn`) p/ SSR ou ops que precisam httpOnly cookie | `@tanstack/react-start`, `./types` |
| `features/*/components/*` | UI específica da feature | Qualquer camada abaixo |
| `shared/lib/api-client.ts` | Único ponto HTTP: injeta token, unwrappa `{ data: T }`, retry em 401 | `shared/types`, `token-store` |

## API client rule

**Nenhuma feature chama `fetch` diretamente.** Todo HTTP passa por `apiClient` (`src/shared/lib/api-client.ts`). Exceções:

- **Uploads via presigned URL** (MinIO/S3): `fetch` PUT direto na URL — credencial na própria URL, não mande `Authorization`.
- **Server functions** (`features/*/server.ts`): rodam no servidor, sem acesso ao `tokenStore`; `fetch` direto com token como argumento.

Padrão:

```ts
// features/videos/api.ts
export function getVideo(videoId: string, signal?: AbortSignal) {
  return apiClient.get<Video>(`/v1/videos/${videoId}`, signal)
}

// features/videos/hooks.ts
export function useVideo(videoId: string) {
  return useQuery({
    queryKey: ['videos', videoId],
    queryFn: ({ signal }) => getVideo(videoId, signal),
  })
}
```

### Responsabilidades do `apiClient`

- Adiciona `Authorization: Bearer <token>` automaticamente quando há token no `tokenStore`
- Serializa body JSON; trata `204 No Content`
- Em **401**: chama `renewTokenCallback` (configurada no `__root.tsx`), seta novo token, retry **uma vez**. Se retry falhar, limpa token e lança `ApiClientError`
- Em erro, lança `ApiClientError { code, message, status }` — sempre use `toastApiError`
- Desserializa envelope `{ data: T }`, retorna `T`

## Estratégia de renderização

| Rota | Estratégia | Notas |
|---|---|---|
| `/` | SSR | Loader prefetch trending |
| `/watch/$videoId` | SSR | Loader usa `fetchVideoSsr` com `accessToken` do contexto |
| `/search` | SSR | |
| `/$username`, `/$username/$channel` | SSR | (ISR futuro) |
| `/upload`, `/dashboard`, `/settings` | Client-only | `beforeLoad` redireciona `/` se não autenticado |

`__root.tsx` resolve token inicial no servidor via `getInitialToken()`, passa pelo router context → rotas SSR autenticadas sem waterfall.

## Formatos de resposta da API

Documentados em `src/shared/types.ts`:

```ts
// Sucesso: sempre { data: T } — apiClient desembrulha para T
// Erro: { code: string; message: string }
// Enums em respostas: { id: number; value: string } (EnumValue). Em requests, envie só o id.
// Paginação videos/comentários: CursorPage<K, T> — { [K]: T[], nextCursor: string | null }
// Paginação playlists: PagedResult<T> — { items: T[], nextCursor: string | null }
// Comentários deletados: mantidos na UI com isDeleted: true, content: null
```

Enums espelhados do backend: `VideoStatus`, `VideoVisibility`, `ReactionType`, `PlaylistVisibility`, `PlaylistScope`, `CommentSortOrder`.

Códigos de erro em `API_ERROR_CODES` — use ao mapear mensagens user-facing em `shared/lib/error-messages.ts`.

## Query key convention

Formato: `['resource-type', id, ...sub]`. Exemplos:

```ts
['videos', videoId]
['videos', 'trending']
['videos', 'feed']
['channels', handle]
['users', 'me']
['channels', channelId, 'videos']
```

Módulo pode exportar `*Keys` p/ centralizar (ver `features/users/hooks.ts`: `userKeys.me()`).

## Environment

```
VITE_API_URL=http://localhost:5000   # .NET backend
```

Exposta ao cliente via `import.meta.env.VITE_API_URL`. No servidor (`server.ts`): `process.env.VITE_API_URL`.