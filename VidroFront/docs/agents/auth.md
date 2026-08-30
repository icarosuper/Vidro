# Auth

Leia antes de mexer em sign in/sign up/sign out, proteção de rota, ou código que toque `tokenStore`, `renewToken`, `apiClient` 401 handling, SSR hydration.

## Modelo

- **Access token** vive **só em memória** (`src/shared/lib/token-store.ts`). Nunca em `localStorage`, nunca em cookie acessível ao JS.
- **Refresh token** em **cookie httpOnly** (`vid_rt`, 30d). Só manipulado por server functions em `features/auth/server.ts`.
- **401 handling:** `apiClient` intercepta → chama `renewTokenCallback` → grava novo token → retry único. Se falhar: limpa token, lança `ApiClientError`.
- **Rotas protegidas** usam `beforeLoad`: redireciona p/ `/` com modal login se sem sessão.
- **Rotas SSR autenticadas** recebem `accessToken` via router context do `__root.tsx` (sem waterfall).

## Arquivos relevantes

| Arquivo | Papel |
|---|---|
| `src/shared/lib/token-store.ts` | Store em memória c/ `subscribe` p/ `useSyncExternalStore` |
| `src/shared/lib/api-client.ts` | Renew + retry em 401, `setRenewTokenCallback` |
| `src/features/auth/server.ts` | `serverSignIn`, `serverSignUp`, `serverSignOut`, `renewToken`, `getInitialToken` |
| `src/features/auth/hooks.tsx` | `AuthProvider`, `AuthModalProvider`, `useIsAuthenticated`, `useSignIn/Up/Out`, `useAuthModal` |
| `src/routes/__root.tsx` | Loader SSR: resolve token inicial + hidratação client |

## Server functions (auth/server.ts)

Todas usam `createServerFn` do `@tanstack/react-start`.

| Função | Método | O que faz |
|---|---|---|
| `serverSignUp` | POST | Cadastra; não retorna token |
| `serverSignIn` | POST | Autentica, grava cookie refresh, retorna access token |
| `serverSignOut` | POST | Revoga refresh no backend, deleta cookie |
| `renewToken` | POST | Usa cookie refresh → novo access token + rotaciona refresh |
| `getInitialToken` | GET | Chamada no loader do `__root`; retorna access token inicial se refresh válido, senão `null` |

Detalhes:
- Refresh token cookie: `httpOnly`, `secure` em produção, `sameSite: 'lax'`, `maxAge: 30d`, `path: '/'`.
- `getInitialToken` limpa cookie se renovação falhar (token inválido/expirado).

## Sign out — ordem crítica

Em `useSignOut`, limpe estado local **antes** de `await serverSignOut(...)`. Garante `useSyncExternalStore` notifique imediatamente, UI atualiza sem esperar servidor.

```ts
// ✅
tokenStore.clear()
apiClient.setRenewTokenCallback(null)
queryClient.clear()
if (accessToken) await serverSignOut({ data: { accessToken } })

// ❌ — UI só atualiza depois que o servidor responder
if (accessToken) await serverSignOut({ data: { accessToken } })
tokenStore.clear()
```

## SSR hydration — sem flash

`__root.tsx` tem `beforeLoad` que chama `getInitialToken()` **só no servidor**, retorna token no context. `loader` repassa ao componente. Token serializado no dehydrated state do router, chega ao cliente sem waterfall.

### `useIsAuthenticated` — server snapshot

Usa `useSyncExternalStore` com 3 argumentos:
1. `subscribe` — reatividade sign in/out pós-mount
2. **client snapshot** — `() => tokenStore.get() !== null`
3. **server snapshot** — `() => initialIsAuthenticated` (injetado via `AuthProvider` context)

Na hidratação, React usa **server snapshot**. Se hardcoded `() => false`, servidor renderiza "não autenticado" e cliente flasha ao corrigir. Solução: `AuthProvider` injeta `initialIsAuthenticated` via context.

### `tokenStore` é client-only

**`tokenStore.set` nunca roda no servidor** — estado global mutável vaza entre requests concorrentes. Inicialização no `RootApp` com guard `typeof window !== 'undefined'`.

```ts
// __root.tsx — dentro do RootApp component
const isClient = typeof window !== 'undefined'
const tokenAlreadySet = tokenStore.get() !== null
if (isClient && !tokenAlreadySet && initialToken !== null) {
  tokenStore.set(initialToken)
  apiClient.setRenewTokenCallback(() => renewToken())
}
```

## Rotas protegidas

```ts
// src/routes/upload.tsx (idem dashboard, settings)
export const Route = createFileRoute('/upload')({
  beforeLoad: () => {
    const notAuthenticated = tokenStore.get() === null
    if (notAuthenticated) throw redirect({ to: '/' })
  },
  component: UploadPage,
})
```

`beforeLoad` roda em server e client, mas `tokenStore` só tem valor no client → guard protege navegação client. P/ proteção em primeira requisição, SSR routes usam `accessToken` do context do root (`context.accessToken`).

## SSR autenticado (padrão de rota)

```ts
// src/routes/watch.$videoId.tsx
export const Route = createFileRoute('/watch/$videoId')({
  loader: async ({ params, context: { queryClient, accessToken } }) => {
    await queryClient.prefetchQuery({
      queryKey: ['videos', params.videoId],
      queryFn: () => fetchVideoSsr(params.videoId, accessToken),
    })
  },
})
```

`accessToken` vem do router context (populado no `beforeLoad` do `__root`). No client, é `null` (`tokenStore` assume controle).

## Hooks (auth/hooks.tsx)

| Hook | Retorno | Observações |
|---|---|---|
| `useIsAuthenticated()` | `boolean` | Reativo via `useSyncExternalStore`; SSR-safe |
| `useSignIn()` | mutation | Em sucesso, seta token e `renewTokenCallback` |
| `useSignUp()` | mutation | Não autentica — só cria conta |
| `useSignOut()` | mutation | Ordem: limpar local → chamar server |
| `useAuthModal()` | `{ isOpen, open, close, mode }` | Modal global login/signup via `AuthModalProvider` |