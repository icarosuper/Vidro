# Conventions

Read doc before write component, hook, API fn. Define non-obvious patterns for all code.

Idioma do código e as regras de legibilidade que valem para os três serviços — variável nomeada em
vez de expressão inline, extrair lógica complexa, nomes que dizem *o quê*, ternário em três linhas —
moram uma vez só no [`../../../CLAUDE.md`](../../../CLAUDE.md) da raiz do monorepo. Este arquivo tem
só o que é específico do front.

## Feature module layout

All feature in `src/features/<name>/` follow:

```
features/<name>/
├── api.ts              # fns que chamam apiClient. Uma fn por endpoint.
├── hooks.ts            # useQuery/useMutation/useInfiniteQuery consumindo api.ts
├── types.ts            # Request/Response/Summary/Entidade
├── server.ts           # (opcional) createServerFn para SSR ou httpOnly cookie
└── components/         # UI específica da feature
```

### Regras

- **`api.ts` never call `fetch` directly** — always via `apiClient`, except presigned uploads (see [architecture.md](architecture.md)).
- **Hooks only consume `api.ts`**, no import `apiClient` or `fetch`.
- **API fns accept `signal?: AbortSignal`** and forward it. Enables React Query auto-cancel.
- **Request shapes** use numeric `id` for enums (e.g. `visibility: number`). **Response shapes** receive `EnumValue`.
- **`*Summary`** = lean type for lists; **`Channel`/`Video`/etc.** = full detail.

### Exemplo: nova endpoint

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

### Mutations + invalidação

Mutations invalidate affected query keys **in `onSuccess`**, use `toastApiError` in `onError`.

```ts
export function useUpdateVideo(videoId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (data: UpdateVideoRequest) => updateVideo(videoId, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['videos', videoId] })
    },
    onError: toastApiError,
  })
}
```

## Query keys

Format: `['resource-type', id, ...sub]`.

```ts
['videos', videoId]
['videos', 'trending']
['videos', 'feed']
['channels', handle]
['users', 'me']
['channels', channelId, 'videos']
```

Keys used in multiple places → export `*Keys` object (e.g. `userKeys.me()` in `features/users/hooks.ts`).

## Índice de array é `T | undefined`

`noUncheckedIndexedAccess` está ligado no `tsconfig.json`. `items[0]` tem tipo
`T | undefined`, mesmo quando você acabou de checar o `length` — o compilador não liga as
duas coisas.

- Em código de produção: `items[0] ?? fallback`, ou `?.` na continuação. É o que o repo já
  fazia antes da flag existir (`video.thumbnailUrls[0] ?? undefined`).
- Em teste: **não** silencie com `!` (o `biome` acusa `noNonNullAssertion`). Destruture e
  falhe com mensagem própria, que também dá um erro legível quando a expectativa quebra:

  ```ts
  const [firstCall] = mockFetch.mock.calls
  if (!firstCall) throw new Error('apiClient did not call fetch')
  const [url, init] = firstCall
  ```

## Error handling

- **API errors** always become `ApiClientError { code, message, status }`.
- **Toasts:** use `toastApiError(error)` from `shared/lib/toast-error.ts`. Translates `code` to user-facing message via `error-messages.ts`.
- **No `try/catch` just for toast** — pass `onError: toastApiError` in `useMutation`.
- **Known codes** in `API_ERROR_CODES` (`shared/types.ts`). React to specific code → import from constant, not string literal.

## Naming

| Tipo | Padrão | Exemplo |
|---|---|---|
| Hook | `useXxx` / `useXxxMutation` | `useVideo`, `useCreateChannel` |
| API fn | verbo + recurso | `getVideo`, `createChannel`, `uploadChannelAvatar` |
| Server fn | `serverXxx` ou verbo descritivo | `serverSignIn`, `getInitialToken`, `fetchVideoSsr` |
| Request type | `XxxRequest` | `CreateVideoRequest` |
| Response type | `XxxResponse` / entidade | `CreateVideoResponse`, `Video` |
| Summary type | `XxxSummary` | `VideoSummary`, `ChannelSummary` |
| Page type | `XxxPage` | `FeedPage`, `CommentsPage` |
| Enum mirror | const as const | `VideoStatus`, `ReactionType` |

## Componentes

- **shadcn/ui** in `src/components/ui/`. Customize by extension, not editing base wrappers.
- **Feature components** in `src/features/<name>/components/`. Shared across features → `src/components/`.
- **Forms:** `react-hook-form` + `zodResolver` + os componentes `Form*` do shadcn
  (`components/ui/form.tsx`). O padrão é o mesmo nos 8 formulários (`SignInForm`,
  `SignUpForm`, `CreateChannelForm`, `EditChannelForm`, `CreatePlaylistForm`,
  `EditPlaylistForm`, `UploadVideoForm`, `EditVideoForm`): schema `zod` no topo do arquivo,
  `type XxxFormValues = z.infer<typeof schema>`, `useForm({ resolver: zodResolver(schema),
  defaultValues })`, um `FormField` por campo e `form.handleSubmit(...)` no `onSubmit`.
  Erro de validação aparece no `FormMessage`; erro da API vem do estado da mutation, via
  `getApiErrorMessage(mutation.error)`. Não componha formulário na mão com `useState`.
- **No props drilling beyond 2 levels** — use context (e.g. `AuthModalProvider`) when needed.

## Imports

Use alias `#/` for `src/`:

```ts
import { apiClient } from '#/shared/lib/api-client'
import { toastApiError } from '#/shared/lib/toast-error'
```

Order (Biome auto-organizes):
1. External libs
2. `#/shared/*`
3. `#/features/*`, `#/components/*`
4. Relative (`./`)
## Checklist — endpoint novo

1. **Confirme a rota** em [`../VidroApi/docs/agents/features-index.md`](../../../VidroApi/docs/agents/features-index.md):
   path exato, arquivo fonte no backend e shape de request/response. Não adivinhe path — o backend
   aninha recurso de canal sob o usuário (`/v1/users/{username}/channels/{handle}/...`).
2. **`types.ts`** — `XxxRequest` / `XxxResponse`. Enum em **response** chega como `EnumValue`
   (`{ id, value }`); em **request** mande só o `id` numérico.
3. **`api.ts`** — uma função por endpoint, sempre via `apiClient`, sempre aceitando e repassando
   `signal?: AbortSignal`. Nunca `fetch` direto (exceções: upload presigned e `server.ts`).
4. **`hooks.ts`** — `useQuery`/`useMutation` consumindo só `api.ts`. Query key no formato
   `['resource-type', id, ...sub]`. Mutation invalida as keys afetadas em `onSuccess` e usa
   `onError: toastApiError`.
5. **Código de erro novo** vindo do backend → registre em `API_ERROR_CODES` (`shared/types.ts`) e
   mapeie a mensagem em `shared/lib/error-messages.ts`. Nunca compare `code` com string literal.
6. **Rota SSR** que precisa desse dado → `server.ts` com `createServerFn`, e o `loader` faz
   `prefetchQuery` com o `accessToken` do router context (ver [auth.md](auth.md)).
7. `bun run test` e `biome check`.
8. **Atualize [`features-index.md`](features-index.md)** — é o mapa que evita grep no próximo ciclo.

## Checklist — feature module novo

1. `src/features/<nome>/` com `api.ts`, `hooks.ts`, `types.ts`, `components/` (e `server.ts` só se
   precisar de SSR ou cookie httpOnly).
2. Componente compartilhado por mais de uma feature não mora aqui — vai para `src/components/`.
3. Rota nova em `src/routes/` (file-based). Rota autenticada client-only usa o guard `beforeLoad`;
   rota SSR autenticada usa `context.accessToken`. Escolha a estratégia e registre na tabela de
   renderização em [architecture.md](architecture.md).
4. O resto é o checklist de endpoint acima, por endpoint.
5. Atualize `features-index.md` e a tabela de renderização em `architecture.md`.
