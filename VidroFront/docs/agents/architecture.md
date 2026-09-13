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
│   │   ├── correlation-id.ts # X-Correlation-ID por requisição
│   │   ├── token-store.ts # access token em memória (client-only)
│   │   ├── error-messages.ts
│   │   ├── seo.ts          # meta tags (title/OG/Twitter) do `head` de cada rota
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
- Adiciona `X-Correlation-ID` (um por requisição, de `shared/lib/correlation-id.ts`)
- Serializa body JSON; trata `204 No Content`
- Em **401**: chama `renewTokenCallback` (configurada no `__root.tsx`), seta novo token, retry **uma vez**. Se retry falhar, limpa token e lança `ApiClientError`
- Em erro, lança `ApiClientError { code, message, status, correlationId }` — sempre use `toastApiError`
- Desserializa envelope `{ data: T }`, retorna `T`

### Correlation ID

Toda chamada de saída leva `X-Correlation-ID`, gerado em `shared/lib/correlation-id.ts`. A API
aceita o header de entrada (`CorrelationIdMiddleware`), devolve no response e carimba o valor em
**toda linha de log daquela requisição** — então o id que o front gera é o que se procura no Loki.

- `ApiClientError.correlationId` guarda o id da requisição que falhou.
- `RouteErrorFallback` mostra esse id ("Reference for support") para o usuário citar. Ele lê a
  propriedade pelo formato, **não** por `instanceof`: erro lançado em loader de SSR chega ao
  browser serializado, sem a classe.
- **As exceções da regra "sem `fetch` direto" também mandam o header** — os dois
  `features/*/server.ts` chamam `newCorrelationId()` na mão. Upload presigned é a única saída sem
  ele, de propósito: vai para o MinIO, não para a API, e a URL assinada não tolera header extra.
- `crypto.randomUUID` é `undefined` em browser fora de contexto seguro (http em host que não é
  localhost); por isso há fallback — é id de correlação, não token de segurança.

### Enums espelhados do backend

`shared/types.ts` redigita seis enums da API (`VideoStatus`, `VideoVisibility`, `ReactionType`,
`PlaylistVisibility`, `PlaylistScope`, `CommentSortOrder`) porque a API os serializa como
**inteiro**. Não invente valor novo e não mude número aqui: o contrato é
[`contracts/enums.json`](../../../contracts/README.md) na raiz do monorepo, e
`src/tests/enum-contract.test.ts` falha se este arquivo divergir dele — assim como o teste do
lado da API. Mudar um membro é mudar o golden e os dois lados no mesmo commit.

O `openapi/v1.json` da API **não** serve para isso: ele descreve as rotas e nenhuma resposta.
Ver `VidroApi/openapi/README.md`.

## Estratégia de renderização

| Rota | Estratégia | Notas |
|---|---|---|
| `/` | SSR | Loader prefetch trending |
| `/watch/$videoId` | SSR | Loader usa `fetchVideoSsr` com `accessToken` do contexto |
| `/search` | SSR | Query em `?q=` (`validateSearch`); sem prefetch no loader |
| `/$username`, `/$username/$channel` | SSR | (ISR futuro) |
| `/upload`, `/dashboard`, `/settings` | Client-only | `beforeLoad` redireciona `/` se não autenticado |

`__root.tsx` resolve token inicial no servidor via `getInitialToken()`, passa pelo router context → rotas SSR autenticadas sem waterfall.

### Fallback de erro e de rota inexistente

`src/router.tsx` registra `defaultErrorComponent` e `defaultNotFoundComponent`
(`components/RouteFallback.tsx`), então **toda** rota herda os dois — URL inválida, `notFound()`
ou throw em loader/render caem numa tela com caminho de volta em vez de tela branca. Uma rota só
declara os seus quando consegue fazer melhor.

Isso cobre erro de **rota**. Erro de **query** continua sendo do componente: trate `isError` do
`useQuery` na própria tela (o padrão do repo é um parágrafo `text-muted-foreground` centrado), porque
o fallback do router não vê uma query que falhou depois da rota já ter renderizado.

## SEO e `head` por rota

Toda rota declara o seu `head` com o helper `seo()` (`src/shared/lib/seo.ts`), que monta title,
description, Open Graph e Twitter card de uma vez. O `__root.tsx` traz o default, e a rota que
não declara nada herda dele.

- **Título:** `seo()` acrescenta `· Vidro` — passe só a parte da página (`'Dashboard'`), nunca o
  título inteiro. `'Vidro'` é o único que não recebe sufixo.
- **Página privada ou de resultado de busca** passa `noIndex: true`.
- **Rota com dado dinâmico** (watch, playlist) lê de `loaderData`, e o loader devolve o que
  pegou do cache com `queryClient.getQueryData` — **não** o resultado de `ensureQueryData`.
  O `prefetchQuery` engole o erro; trocar por uma query que lança transformaria meta tag quebrada
  em página quebrada.
- **`og:url` não é emitido**: não existe URL pública configurada no projeto, e o crawler cai na
  URL que ele mesmo buscou. Quando existir domínio, é aqui que entra.
- **`og:image` do watch é URL presignada do MinIO** e portanto expira — card cacheado por tempo
  demais perde a imagem. Resolver de verdade exige artefato público ou URL de proxy.

O idioma da UI é **inglês** (`<html lang="en">`); datas usam `toLocaleDateString('en-US')`.

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