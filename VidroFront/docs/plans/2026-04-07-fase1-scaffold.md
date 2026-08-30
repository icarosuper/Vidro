I'll construct the fixed file by ensuring all code blocks match the ORIGINAL exactly. Let me output the corrected compressed file now:

# Fase 1 — Scaffold Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Criar projeto TanStack Start completo em `FrontNovo/` — pastas, API client tipado, rotas placeholder prontos p/ Fase 2 (Auth).

**Architecture:** TanStack Start (SSR seletivo) + TanStack Query, shadcn/ui + Tailwind v4. Fetch wrapper central em `src/shared/lib/api-client.ts` — nenhuma feature faz `fetch` direto. Estrutura vertical-slice em `src/features/`.

**Tech Stack:** TanStack Start, TanStack Query, shadcn/ui, Tailwind CSS v4, TypeScript, bun, Vitest

> **Nota:** CLI TanStack gera arquivos em `src/` (não `app/`). Usar `src/` em todos caminhos abaixo.

---

## ✅ Task 1: Scaffold do projeto

**Files:**
- Create: `FrontNovo/` (projeto completo)

**Step 1: Rodar CLI TanStack Start**

No diretório `/home/icaro/Projetos/Vidro/`:

```bash
bunx @tanstack/cli create FrontNovo \
  --package-manager bun \
  --no-examples \
  --add-ons shadcn,tanstack-query \
  --no-git
```

CLI pergunta sobre toolchain — escolher **biome** (linter + formatter, sem config extra).

**Step 2: Verificar projeto sobe**

```bash
cd FrontNovo
bun run dev
```

Esperado: servidor em `http://localhost:3000` sem erros. Fechar após verificar.

**Step 3: Commit inicial**

```bash
git init
git add -A
git commit -m "chore: scaffold TanStack Start com shadcn e TanStack Query"
```

---

## ✅ Task 2: Limpar boilerplate

CLI gera páginas de exemplo, componentes demo e estilos temáticos (cores `--sea-ink`, `--lagoon`, etc.) — não queremos. Substituir por base neutra.

**Files:**
- Delete: `src/routes/about.tsx`
- Modify: `src/routes/index.tsx`
- Modify: `src/components/Header.tsx`
- Delete: `src/components/Footer.tsx`
- Modify: `src/styles.css`
- Modify: `src/routes/__root.tsx`

**Step 1: Remover rota `/about`**

```bash
rm src/routes/about.tsx
```

**Step 2: Resetar `src/routes/index.tsx`**

Substituir todo conteúdo por:

```tsx
import { createFileRoute } from '@tanstack/react-router'

export const Route = createFileRoute('/')({
  component: HomePage,
})

function HomePage() {
  return (
    <main className="page-container py-8">
      <h1 className="text-2xl font-bold">Vidro</h1>
    </main>
  )
}
```

**Step 3: Resetar `src/components/Header.tsx`**

```tsx
import { Link } from '@tanstack/react-router'

export function Header() {
  return (
    <header className="border-b border-border bg-background">
      <div className="page-container flex h-14 items-center justify-between">
        <Link to="/" className="text-lg font-bold text-foreground no-underline">
          Vidro
        </Link>
      </div>
    </header>
  )
}
```

**Step 4: Remover Footer**

```bash
rm src/components/Footer.tsx
```

**Step 5: Resetar `src/styles.css`**

Substituir todo conteúdo (manter só necessário p/ shadcn + Tailwind v4):

```css
@import 'tailwindcss';
@import 'tw-animate-css';

@custom-variant dark (&:is(.dark *));

:root {
  --background: oklch(1 0 0);
  --foreground: oklch(0.141 0.005 285.823);
  --card: oklch(1 0 0);
  --card-foreground: oklch(0.141 0.005 285.823);
  --popover: oklch(1 0 0);
  --popover-foreground: oklch(0.141 0.005 285.823);
  --primary: oklch(0.21 0.006 285.885);
  --primary-foreground: oklch(0.985 0 0);
  --secondary: oklch(0.967 0.001 286.375);
  --secondary-foreground: oklch(0.21 0.006 285.885);
  --muted: oklch(0.967 0.001 286.375);
  --muted-foreground: oklch(0.552 0.016 285.938);
  --accent: oklch(0.967 0.001 286.375);
  --accent-foreground: oklch(0.21 0.006 285.885);
  --destructive: oklch(0.577 0.245 27.325);
  --destructive-foreground: oklch(0.577 0.245 27.325);
  --border: oklch(0.92 0.004 286.32);
  --input: oklch(0.92 0.004 286.32);
  --ring: oklch(0.871 0.006 286.286);
  --radius: 0.625rem;
}

.dark {
  --background: oklch(0.141 0.005 285.823);
  --foreground: oklch(0.985 0 0);
  --card: oklch(0.141 0.005 285.823);
  --card-foreground: oklch(0.985 0 0);
  --popover: oklch(0.141 0.005 285.823);
  --popover-foreground: oklch(0.985 0 0);
  --primary: oklch(0.985 0 0);
  --primary-foreground: oklch(0.21 0.006 285.885);
  --secondary: oklch(0.274 0.006 286.033);
  --secondary-foreground: oklch(0.985 0 0);
  --muted: oklch(0.274 0.006 286.033);
  --muted-foreground: oklch(0.705 0.015 286.067);
  --accent: oklch(0.274 0.006 286.033);
  --accent-foreground: oklch(0.985 0 0);
  --destructive: oklch(0.396 0.141 25.723);
  --destructive-foreground: oklch(0.637 0.237 25.331);
  --border: oklch(0.274 0.006 286.033);
  --input: oklch(0.274 0.006 286.033);
  --ring: oklch(0.442 0.017 285.786);
}

@theme inline {
  --font-sans: ui-sans-serif, system-ui, sans-serif;
  --color-background: var(--background);
  --color-foreground: var(--foreground);
  --color-card: var(--card);
  --color-card-foreground: var(--card-foreground);
  --color-popover: var(--popover);
  --color-popover-foreground: var(--popover-foreground);
  --color-primary: var(--primary);
  --color-primary-foreground: var(--primary-foreground);
  --color-secondary: var(--secondary);
  --color-secondary-foreground: var(--secondary-foreground);
  --color-muted: var(--muted);
  --color-muted-foreground: var(--muted-foreground);
  --color-accent: var(--accent);
  --color-accent-foreground: var(--accent-foreground);
  --color-destructive: var(--destructive);
  --color-destructive-foreground: var(--destructive-foreground);
  --color-border: var(--border);
  --color-input: var(--input);
  --color-ring: var(--ring);
  --radius-sm: calc(var(--radius) - 4px);
  --radius-md: calc(var(--radius) - 2px);
  --radius-lg: var(--radius);
  --radius-xl: calc(var(--radius) + 4px);
}

@layer base {
  * {
    @apply border-border outline-ring/50;
  }
  body {
    @apply bg-background text-foreground;
    -webkit-font-smoothing: antialiased;
  }
}

.page-container {
  width: min(1280px, calc(100% - 2rem));
  margin-inline: auto;
}
```

**Step 6: Atualizar `src/routes/__root.tsx`**

Remover refs ao Footer e tema oceânico. Manter `Header`, providers, devtools:

```tsx
import {
  HeadContent,
  Scripts,
  createRootRouteWithContext,
} from '@tanstack/react-router'
import { TanStackRouterDevtoolsPanel } from '@tanstack/react-router-devtools'
import { TanStackDevtools } from '@tanstack/react-devtools'
import { Outlet } from '@tanstack/react-router'
import { Header } from '../components/Header'
import TanStackQueryDevtools from '../integrations/tanstack-query/devtools'
import appCss from '../styles.css?url'
import type { QueryClient } from '@tanstack/react-query'

interface MyRouterContext {
  queryClient: QueryClient
}

export const Route = createRootRouteWithContext<MyRouterContext>()({
  head: () => ({
    meta: [
      { charSet: 'utf-8' },
      { name: 'viewport', content: 'width=device-width, initial-scale=1' },
      { title: 'Vidro' },
    ],
    links: [{ rel: 'stylesheet', href: appCss }],
  }),
  shellComponent: RootDocument,
})

function RootDocument({ children }: { children: React.ReactNode }) {
  return (
    <html lang="pt-BR">
      <head>
        <HeadContent />
      </head>
      <body>
        <Header />
        <Outlet />
        <TanStackDevtools
          config={{ position: 'bottom-right' }}
          plugins={[
            { name: 'Tanstack Router', render: <TanStackRouterDevtoolsPanel /> },
            TanStackQueryDevtools,
          ]}
        />
        <Scripts />
      </body>
    </html>
  )
}
```

**Step 7: Verificar funciona**

```bash
bun run dev
```

Esperado: página inicial com header "Vidro" e fundo branco/neutro.

**Step 8: Commit**

```bash
git add -A
git commit -m "chore: limpar boilerplate do scaffold"
```

---

## ✅ Task 3: Criar estrutura de pastas das features

**Files:**
- Create: `src/features/auth/components/.gitkeep`
- Create: `src/features/auth/hooks.ts`
- Create: `src/features/auth/server.ts`
- Create: `src/features/auth/types.ts`
- Create: `src/features/videos/components/.gitkeep`
- Create: `src/features/videos/hooks.ts`
- Create: `src/features/videos/api.ts`
- Create: `src/features/videos/types.ts`
- Create: `src/features/channels/components/.gitkeep`
- Create: `src/features/channels/hooks.ts`
- Create: `src/features/channels/api.ts`
- Create: `src/features/channels/types.ts`
- Create: `src/features/comments/components/.gitkeep`
- Create: `src/features/comments/hooks.ts`
- Create: `src/features/comments/api.ts`
- Create: `src/features/comments/types.ts`
- Create: `src/features/playlists/components/.gitkeep`
- Create: `src/features/playlists/hooks.ts`
- Create: `src/features/playlists/api.ts`
- Create: `src/features/playlists/types.ts`
- Create: `src/features/users/components/.gitkeep`
- Create: `src/features/users/hooks.ts`
- Create: `src/features/users/api.ts`
- Create: `src/features/users/types.ts`
- Modify: `src/shared/` (já existe — adicionar subpastas)

**Step 1: Criar estrutura em único comando**

```bash
mkdir -p \
  src/features/auth/components \
  src/features/videos/components \
  src/features/channels/components \
  src/features/comments/components \
  src/features/playlists/components \
  src/features/users/components \
  src/shared/components/ui \
  src/shared/lib

# Criar arquivos vazios (mas válidos TypeScript) para cada feature
for feature in auth videos channels comments playlists users; do
  touch src/features/$feature/types.ts
  touch src/features/$feature/api.ts
  touch src/features/$feature/hooks.ts
  [ "$feature" = "auth" ] && touch src/features/auth/server.ts
done
```

**Step 2: Commit**

```bash
git add -A
git commit -m "chore: criar estrutura de pastas das features"
```

---

## ✅ Task 4: Definir tipos base compartilhados

**Files:**
- Create: `src/shared/types.ts`

**Step 1: Criar `src/shared/types.ts`**

```ts
// Wrapper de sucesso para todas as respostas da API
export type ApiSuccess<T> = { data: T }

// Erro de domínio (400/401/403/404/409)
export type ApiError = {
  code: string
  message: string
}

// Erro de validação (400 — FluentValidation)
export type ValidationError = {
  errors: Array<{ field: string; message: string }>
}

// Toda enum na resposta da API vem como EnumValue
// Nos requests, enviar apenas o id (número inteiro)
export type EnumValue = {
  id: number
  value: string
}

// Página paginada por cursor — estilo usado em vídeos, comentários e replies
// K é o nome do campo do array (ex: 'videos', 'comments', 'replies')
export type CursorPage<K extends string, T> = {
  [key in K]: T[]
} & {
  nextCursor: string | null
}

// Página paginada por cursor — estilo usado em playlists (PagedResult<T>)
export type PagedResult<T> = {
  items: T[]
  nextCursor: string | null
}

// Códigos de erro conhecidos da API
export const API_ERROR_CODES = {
  UNAUTHORIZED: 'request.unauthorized',
  FORBIDDEN: 'request.forbidden',
  NOT_FOUND: 'resource.not_found',
  EMAIL_CONFLICT: 'user.email_conflict',
  USERNAME_CONFLICT: 'user.username_conflict',
  INVALID_CREDENTIALS: 'user.invalid_credentials',
  REFRESH_TOKEN_NOT_FOUND: 'refresh_token.not_found',
  REFRESH_TOKEN_EXPIRED: 'refresh_token.expired',
  REFRESH_TOKEN_REVOKED: 'refresh_token.revoked',
  CHANNEL_NOT_OWNER: 'channel.not_owner',
  CHANNEL_LIMIT_REACHED: 'channel.limit_reached',
  CHANNEL_CANNOT_FOLLOW_OWN: 'channel.cannot_follow_own',
  CHANNEL_ALREADY_FOLLOWING: 'channel.already_following',
  CHANNEL_NOT_FOLLOWING: 'channel.not_following',
  VIDEO_NOT_OWNER: 'video.not_owner',
  VIDEO_CANNOT_REACT_OWN: 'video.cannot_react_to_own',
  COMMENT_NOT_OWNER: 'comment.not_owner',
  COMMENT_REPLY_NESTING: 'comment.reply_nesting_not_allowed',
  PLAYLIST_NOT_OWNER: 'playlist.not_owner',
  PLAYLIST_VIDEO_ALREADY_IN: 'playlist.video_already_in_playlist',
  PLAYLIST_VIDEO_NOT_IN: 'playlist.video_not_in_playlist',
} as const

export type ApiErrorCode = (typeof API_ERROR_CODES)[keyof typeof API_ERROR_CODES]

// Enums espelhando os da API
export const VideoStatus = { PendingUpload: 0, Processing: 1, Ready: 2, Failed: 3 } as const
export const VideoVisibility = { Public: 0, Unlisted: 1, Private: 2 } as const
export const ReactionType = { Like: 1, Dislike: 2 } as const
export const PlaylistVisibility = { Public: 0, Private: 1 } as const
export const PlaylistScope = { User: 0, Channel: 1 } as const
export const CommentSortOrder = { Recent: 0, Popular: 1 } as const
```

**Step 2: Commit**

```bash
git add src/shared/types.ts
git commit -m "feat: definir tipos base compartilhados (ApiSuccess, ApiError, EnumValue, etc)"
```

---

## ✅ Task 5: Implementar o API client

Client central faz `fetch` p/ API, injeta access token, intercepta 401 p/ renovar token automaticamente, mapeia erros p/ tipos TypeScript.

Access token fica em memória (módulo singleton). Refresh via server function conectado na Fase 2 — por ora client recebe callback `onRenewToken` que pode ser null.

**Files:**
- Create: `src/shared/lib/api-client.ts`
- Create: `src/shared/lib/token-store.ts`
- Create: `src/tests/api-client.test.ts`

**Step 1: Criar `src/shared/lib/token-store.ts`**

Store de access token em memória — nunca persiste em localStorage/cookie (segurança).

```ts
let accessToken: string | null = null

export const tokenStore = {
  get: () => accessToken,
  set: (token: string | null) => {
    accessToken = token
  },
  clear: () => {
    accessToken = null
  },
}
```

**Step 2: Escrever testes antes de implementar o client**

Criar `src/tests/api-client.test.ts`:

```ts
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { tokenStore } from '#/shared/lib/token-store'

// Mock do fetch global
const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

// Helper para criar respostas mock
function mockResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

beforeEach(() => {
  tokenStore.clear()
  mockFetch.mockReset()
})

afterEach(() => {
  vi.restoreAllMocks()
})

describe('apiClient', () => {
  it('faz GET sem token quando não há sessão', async () => {
    const { apiClient } = await import('#/shared/lib/api-client')
    mockFetch.mockResolvedValueOnce(mockResponse({ data: { id: '1' } }))

    const result = await apiClient.get('/v1/test')

    expect(mockFetch).toHaveBeenCalledOnce()
    const [url, init] = mockFetch.mock.calls[0] as [string, RequestInit]
    expect(url).toContain('/v1/test')
    expect((init.headers as Record<string, string>)['Authorization']).toBeUndefined()
    expect(result).toEqual({ id: '1' })
  })

  it('injeta o Authorization header quando há token', async () => {
    tokenStore.set('meu-access-token')
    const { apiClient } = await import('#/shared/lib/api-client')
    mockFetch.mockResolvedValueOnce(mockResponse({ data: { id: '1' } }))

    await apiClient.get('/v1/test')

    const [, init] = mockFetch.mock.calls[0] as [string, RequestInit]
    expect((init.headers as Record<string, string>)['Authorization']).toBe('Bearer meu-access-token')
  })

  it('lança ApiError tipado em resposta de erro', async () => {
    const { apiClient, ApiClientError } = await import('#/shared/lib/api-client')
    mockFetch.mockResolvedValueOnce(
      mockResponse({ code: 'resource.not_found', message: 'Not found' }, 404),
    )

    await expect(apiClient.get('/v1/missing')).rejects.toThrow(ApiClientError)
    await expect(apiClient.get('/v1/missing')).rejects.toMatchObject({
      code: 'resource.not_found',
      status: 404,
    })
  })

  it('tenta renovar o token uma vez em caso de 401', async () => {
    tokenStore.set('token-expirado')
    const { apiClient } = await import('#/shared/lib/api-client')

    const renewToken = vi.fn().mockResolvedValue('token-novo')
    apiClient.setRenewTokenCallback(renewToken)

    mockFetch
      .mockResolvedValueOnce(mockResponse({ code: 'request.unauthorized', message: 'Unauthorized' }, 401))
      .mockResolvedValueOnce(mockResponse({ data: { id: '1' } }))

    const result = await apiClient.get('/v1/test')

    expect(renewToken).toHaveBeenCalledOnce()
    expect(tokenStore.get()).toBe('token-novo')
    expect(result).toEqual({ id: '1' })
  })

  it('lança erro e limpa o token quando a renovação falha', async () => {
    tokenStore.set('token-expirado')
    const { apiClient, ApiClientError } = await import('#/shared/lib/api-client')

    const renewToken = vi.fn().mockRejectedValue(new Error('refresh failed'))
    apiClient.setRenewTokenCallback(renewToken)

    mockFetch.mockResolvedValueOnce(
      mockResponse({ code: 'request.unauthorized', message: 'Unauthorized' }, 401),
    )

    await expect(apiClient.get('/v1/test')).rejects.toThrow(ApiClientError)
    expect(tokenStore.get()).toBeNull()
  })

  it('faz POST com body JSON', async () => {
    const { apiClient } = await import('#/shared/lib/api-client')
    mockFetch.mockResolvedValueOnce(mockResponse({ data: { videoId: 'abc' } }, 201))

    const result = await apiClient.post('/v1/users/joao/channels/meu-canal/videos', {
      title: 'Meu Vídeo',
      tags: [],
      visibility: 0,
    })

    const [, init] = mockFetch.mock.calls[0] as [string, RequestInit]
    expect(init.method).toBe('POST')
    expect(JSON.parse(init.body as string)).toEqual({ title: 'Meu Vídeo', tags: [], visibility: 0 })
    expect(result).toEqual({ videoId: 'abc' })
  })

  it('retorna undefined em respostas 204', async () => {
    const { apiClient } = await import('#/shared/lib/api-client')
    mockFetch.mockResolvedValueOnce(new Response(null, { status: 204 }))

    const result = await apiClient.delete('/v1/users/joao/channels/meu-canal/follow')

    expect(result).toBeUndefined()
  })
})
```

**Step 3: Rodar testes p/ confirmar que falham**

```bash
bun run test src/tests/api-client.test.ts
```

Esperado: todos falhando com "Cannot find module '#/shared/lib/api-client'".

**Step 4: Implementar `src/shared/lib/api-client.ts`**

```ts
import type { ApiError } from '#/shared/types'
import { tokenStore } from './token-store'

export class ApiClientError extends Error {
  constructor(
    public readonly code: string,
    public readonly message: string,
    public readonly status: number,
  ) {
    super(message)
    this.name = 'ApiClientError'
  }
}

type RenewTokenCallback = () => Promise<string>

const BASE_URL = import.meta.env.VITE_API_URL ?? 'http://localhost:5000'

let renewTokenCallback: RenewTokenCallback | null = null

async function request<T>(
  method: string,
  path: string,
  body?: unknown,
  signal?: AbortSignal,
  isRetry = false,
): Promise<T> {
  const token = tokenStore.get()

  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
  }
  if (token) {
    headers['Authorization'] = `Bearer ${token}`
  }

  const response = await fetch(`${BASE_URL}${path}`, {
    method,
    headers,
    body: body !== undefined ? JSON.stringify(body) : undefined,
    signal,
  })

  // 204 No Content
  if (response.status === 204) {
    return undefined as T
  }

  // 401 — tentar renovar o token uma vez
  if (response.status === 401 && !isRetry && renewTokenCallback) {
    try {
      const newToken = await renewTokenCallback()
      tokenStore.set(newToken)
      return request<T>(method, path, body, signal, true)
    } catch {
      tokenStore.clear()
      const data = (await response.json()) as ApiError
      throw new ApiClientError(data.code, data.message, response.status)
    }
  }

  const data = await response.json()

  if (!response.ok) {
    const error = data as ApiError
    throw new ApiClientError(error.code, error.message, response.status)
  }

  // Todas as respostas de sucesso têm formato { data: T }
  return (data as { data: T }).data
}

export const apiClient = {
  get: <T>(path: string, signal?: AbortSignal) =>
    request<T>('GET', path, undefined, signal),

  post: <T>(path: string, body?: unknown, signal?: AbortSignal) =>
    request<T>('POST', path, body, signal),

  put: <T>(path: string, body?: unknown, signal?: AbortSignal) =>
    request<T>('PUT', path, body, signal),

  delete: <T>(path: string, signal?: AbortSignal) =>
    request<T>('DELETE', path, undefined, signal),

  setRenewTokenCallback: (cb: RenewTokenCallback | null) => {
    renewTokenCallback = cb
  },
}
```

**Step 5: Adicionar `VITE_API_URL` ao `.env`**

Criar `FrontNovo/.env`:
```
VITE_API_URL=http://localhost:5000
```

Criar `FrontNovo/.env.example`:
```
VITE_API_URL=http://localhost:5000
```

**Step 6: Rodar testes**

```bash
bun run test src/tests/api-client.test.ts
```

Esperado: todos 7 testes passando.

**Step 7: Commit**

```bash
git add src/shared/lib/api-client.ts src/shared/lib/token-store.ts src/tests/api-client.test.ts .env.example
git commit -m "feat: implementar API client com injeção de token e retry em 401"
```

---

## ✅ Task 6: Criar rotas placeholder

Criar todas rotas do design p/ router reconhecer. Cada rota tem componente mínimo com nome da página.

**Files:**
- Create: `src/routes/watch.$videoId.tsx`
- Create: `src/routes/search.tsx`
- Create: `src/routes/$username.tsx`
- Create: `src/routes/$username.$channel.tsx`
- Create: `src/routes/upload.tsx`
- Create: `src/routes/settings.tsx`
- Create: `src/routes/dashboard.tsx`

**Step 1: Criar cada rota**

`src/routes/watch.$videoId.tsx`:
```tsx
import { createFileRoute } from '@tanstack/react-router'

export const Route = createFileRoute('/watch/$videoId')({
  component: WatchPage,
})

function WatchPage() {
  const { videoId } = Route.useParams()
  return <main className="page-container py-8"><p>Watch: {videoId}</p></main>
}
```

`src/routes/search.tsx`:
```tsx
import { createFileRoute } from '@tanstack/react-router'

export const Route = createFileRoute('/search')({
  component: SearchPage,
})

function SearchPage() {
  return <main className="page-container py-8"><p>Search</p></main>
}
```

`src/routes/$username.tsx`:
```tsx
import { createFileRoute } from '@tanstack/react-router'

export const Route = createFileRoute('/$username')({
  component: UserPage,
})

function UserPage() {
  const { username } = Route.useParams()
  return <main className="page-container py-8"><p>Usuário: {username}</p></main>
}
```

`src/routes/$username.$channel.tsx`:
```tsx
import { createFileRoute } from '@tanstack/react-router'

export const Route = createFileRoute('/$username/$channel')({
  component: ChannelPage,
})

function ChannelPage() {
  const { username, channel } = Route.useParams()
  return <main className="page-container py-8"><p>Canal: @{username}/{channel}</p></main>
}
```

`src/routes/upload.tsx`:
```tsx
import { createFileRoute } from '@tanstack/react-router'

export const Route = createFileRoute('/upload')({
  component: UploadPage,
})

function UploadPage() {
  return <main className="page-container py-8"><p>Upload</p></main>
}
```

`src/routes/settings.tsx`:
```tsx
import { createFileRoute } from '@tanstack/react-router'

export const Route = createFileRoute('/settings')({
  component: SettingsPage,
})

function SettingsPage() {
  return <main className="page-container py-8"><p>Settings</p></main>
}
```

`src/routes/dashboard.tsx`:
```tsx
import { createFileRoute } from '@tanstack/react-router'

export const Route = createFileRoute('/dashboard')({
  component: DashboardPage,
})

function DashboardPage() {
  return <main className="page-container py-8"><p>Dashboard</p></main>
}
```

**Step 2: Verificar rotas funcionam**

```bash
bun run dev
```

Navegar p/ cada rota no browser:
- `http://localhost:3000/` — "Vidro"
- `http://localhost:3000/watch/test-id` — "Watch: test-id"
- `http://localhost:3000/search` — "Search"
- `http://localhost:3000/joao` — "Usuário: joao"
- `http://localhost:3000/joao/meu-canal` — "Canal: joao/meu-canal"
- `http://localhost:3000/upload` — "Upload"
- `http://localhost:3000/settings` — "Settings"
- `http://localhost:3000/dashboard` — "Dashboard"

**Step 3: Commit final da fase**

```bash
git add -A
git commit -m "feat: criar rotas placeholder para todas as páginas do design"
```

---

## Checklist de conclusão da Fase 1

- [x] `bun run dev` sobe sem erros
- [x] `bun run test` passa (7 testes do api-client)
- [x] Todas 8 rotas navegáveis no browser
- [x] Estrutura `src/features/` e `src/shared/` criada
- [x] `src/shared/types.ts` com todos tipos base
- [x] `src/shared/lib/api-client.ts` com token injection e retry 401
- [x] `.env.example` commitado, `.env` no `.gitignore`
- [x] 5 commits limpos no histórico git