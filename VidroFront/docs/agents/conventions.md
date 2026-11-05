# Conventions

Read this doc before writing a component, hook or API function. It defines the non-obvious patterns for all code.

Language (everything in English) and the readability rules shared by all three services — named
variable instead of inline expression, extract complex logic, names that say *what*, ternary
formatting — live once in the monorepo root [`../../../CLAUDE.md`](../../../CLAUDE.md). This file
holds only what is specific to the front.

## Feature module layout

Every feature in `src/features/<name>/` follows:

```
features/<name>/
├── api.ts              # functions that call apiClient. One function per endpoint.
├── hooks.ts            # useQuery/useMutation/useInfiniteQuery consuming api.ts
├── types.ts            # barrel: export type { ... } from '#/shared/api' — never hand-written shapes
├── server.ts           # (optional) createServerFn for SSR or the httpOnly cookie
└── components/         # feature-specific UI
```

### Rules

- **`api.ts` never calls `fetch` directly** — always via `apiClient`, except presigned uploads (see [architecture.md](architecture.md)).
- **Hooks only consume `api.ts`**; they do not import `apiClient` or `fetch`.
- **API functions accept `signal?: AbortSignal`** and forward it. This enables React Query's automatic cancellation.
- **A feature's `types.ts` is a barrel over `#/shared/api`**, never hand-written shapes. A new shape
  means adding an alias in `src/shared/api/types.ts` over `components['schemas'][...]` and
  re-exporting it from the feature's `types.ts` (see "Generated API types" in
  [architecture.md](architecture.md)).
- **Request shapes** type enums as the exact ids (e.g. `visibility: 0 | 1 | 2`), not `number`. To turn
  a `<Select>` string into one, use `toEnumMember(VideoVisibility, value)` from
  `#/shared/lib/enum-member` — it throws on an unknown value. **Response shapes** receive `EnumValue`.
- **`*Summary`** = lean type for lists; **`Channel`/`Video`/etc.** = full detail.

### Example: new endpoint

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

### Query that depends on a parameter that may be missing

`queryFn` receives `skipToken` instead of using `enabled: !!param`. With `enabled`, TypeScript still
thinks the parameter may be `undefined` inside the `queryFn`, and the way out was `param!` — a
promise to the compiler that nobody checks. With `skipToken` the type itself proves the `queryFn`
only exists when the parameter exists, and the query ends up in the same state `enabled: false` left it.

```ts
export function useChannel(
  username: string | undefined,
  handle: string | undefined,
) {
  return useQuery({
    queryKey: channelKeys.detail(username ?? '', handle ?? ''),
    queryFn:
      username && handle
        ? ({ signal }) => getChannel(username, handle, signal)
        : skipToken,
  })
}
```

The query key still needs a value — hence the `?? ''`. The same applies to `useInfiniteQuery`.

### Mutations + invalidation

Mutations invalidate the affected query keys **in `onSuccess`** and use `toastApiError` in `onError`.

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

Keys used in multiple places → export a `*Keys` object (e.g. `userKeys.me()` in `features/users/hooks.ts`).

## Array index is `T | undefined`

`noUncheckedIndexedAccess` is on in `tsconfig.json`. `items[0]` has type
`T | undefined`, even right after you checked `length` — the compiler does not connect the
two.

- In production code: `items[0] ?? fallback`, or `?.` on what follows. That is what the repo
  already did before the flag existed (`video.thumbnailUrls[0] ?? undefined`).
- In tests: do **not** silence it with `!` (`biome` flags `noNonNullAssertion`). Destructure and
  fail with your own message, which also gives a readable error when the expectation breaks:

  ```ts
  const [firstCall] = mockFetch.mock.calls
  if (!firstCall) throw new Error('apiClient did not call fetch')
  const [url, init] = firstCall
  ```

## Component tests

API tests mock `fetch`; component tests mock the feature's **hooks module** and render with
`@testing-library/react`. That way the test exercises the rendering policy (who sees which button,
what shows up on error) without `QueryClientProvider`, without a server and without `waitFor`.

- **`// @vitest-environment jsdom`** on the file's first line. The default stays `node`, which is
  what the API tests want.
- **`cleanup()` in `afterEach`** — this repo does not use Vitest `globals`, so Testing Library's
  automatic cleanup does not run on its own.
- **`vitest.config.ts` exists for a reason:** the TanStack Start plugin resolves React through the
  `react-server` condition, and under Vitest that yields a React with a null hooks dispatcher —
  every `render` dies in `useState`. The test config loads only `tsconfigPaths` + `viteReact`.
- Query by role and accessible name (`getByRole('button', { name: 'Edit' })`), not by class.
  An icon-only button has no accessible name — if the test cannot find it, neither can a screen
  reader, and the fix belongs in the component.
- A component with a TanStack Router `<Link>` needs a router context to render.
  Instead of mounting a router, swap `Link` for an `<a>` with
  `vi.mock('@tanstack/react-router', ...)` — example in
  [`src/tests/playlist-components.test.tsx`](../../src/tests/playlist-components.test.tsx).

Full example: [`src/tests/comment-list.test.tsx`](../../src/tests/comment-list.test.tsx).

## Error handling

- **API errors** always become `ApiClientError { code, message, status }`.
- **Toasts:** use `toastApiError(error)` from `shared/lib/toast-error.ts`. It translates `code` into a user-facing message via `error-messages.ts`.
- **No `try/catch` just for a toast** — pass `onError: toastApiError` in `useMutation`.
- **Known codes** are in `API_ERROR_CODES` (`shared/types.ts`). To react to a specific code → import it from the constant, not a string literal.

## Naming

| Kind | Pattern | Example |
|---|---|---|
| Hook | `useXxx` / `useXxxMutation` | `useVideo`, `useCreateChannel` |
| API fn | verb + resource | `getVideo`, `createChannel`, `uploadChannelAvatar` |
| Server fn | `serverXxx` or a descriptive verb | `serverSignIn`, `getInitialToken`, `fetchVideoSsr` |
| Request type | `XxxRequest` | `CreateVideoRequest` |
| Response type | `XxxResponse` / entity | `CreateVideoResponse`, `Video` |
| Summary type | `XxxSummary` | `VideoSummary`, `ChannelSummary` |
| Page type | `XxxPage` | `FeedPage`, `CommentsPage` |
| Enum mirror | const as const | `VideoStatus`, `ReactionType` |

## Components

- **shadcn/ui** in `src/components/ui/`. Customize by extension, not by editing the base wrappers.
- **Feature components** in `src/features/<name>/components/`. Shared across features → `src/components/`.
- **Forms:** `react-hook-form` + `zodResolver`, `zod` schema at the top of the file and
  `type XxxFormValues = z.infer<typeof schema>`. Do not build a form by hand with `useState`.
  Two styles coexist, and the difference is real:
  - `SignInForm` and `SignUpForm` use shadcn's `Form*` components (`components/ui/form.tsx`):
    `useForm` → `<Form {...form}>`, one `FormField` per field, error in `FormMessage`.
  - The other 6 (`CreateChannelForm`, `EditChannelForm`, `CreatePlaylistForm`,
    `EditPlaylistForm`, `UploadVideoForm`, `EditVideoForm`) **do not use** `FormField`: they destructure
    `register`/`handleSubmit`/`formState.errors` from `useForm`, and each field is `Label` + `Input`
    with `{...register('field')}` + `errors.field.message` in a `<p>`. Follow the style of the file
    you are editing; do not migrate one to the other in passing.
  - In both: the schema's `.max()` always has a message that states the limit (`Name must be at most
    ${NAME_MAX} characters`, limit in a constant); an optional description goes to the API as
    `description?.trim() || null` (empty or whitespace-only = `null`, never `''`); the API error comes
    from the mutation state, via `getApiErrorMessage(mutation.error)`.
- **No props drilling beyond 2 levels** — use context (e.g. `AuthModalProvider`) when needed.

## Imports

Use the `#/` alias for `src/`:

```ts
import { apiClient } from '#/shared/lib/api-client'
import { toastApiError } from '#/shared/lib/toast-error'
```

Order (Biome auto-organizes):
1. External libs
2. `#/shared/*`
3. `#/features/*`, `#/components/*`
4. Relative (`./`)

## Checklist — new endpoint

1. **Confirm the route** in [`../VidroApi/docs/agents/features-index.md`](../../../VidroApi/docs/agents/features-index.md):
   exact path and backend source file. Do not guess the path — the backend nests channel resources
   under the user (`/v1/users/{username}/channels/{handle}/...`).
2. **Types** — add the alias in `src/shared/api/types.ts` over `components['schemas'][...]`
   (`XxxRequest` / `XxxResponse`) and re-export it from the feature's `types.ts`. An enum in a
   **response** arrives as `EnumValue` (`{ id, value }`); in a **request** it is the exact id union
   (`0 | 1 | 2`) — convert a `<Select>` string with `toEnumMember`. If the API record is missing or
   wrong, fix the API side and regenerate (`v1.json`, then `bun run openapi`) — never hand-write the shape.
3. **`api.ts`** — one function per endpoint, always via `apiClient`, always accepting and forwarding
   `signal?: AbortSignal`. Never direct `fetch` (exceptions: presigned upload and `server.ts`).
4. **`hooks.ts`** — `useQuery`/`useMutation` consuming only `api.ts`. Query key in the format
   `['resource-type', id, ...sub]`. A mutation invalidates the affected keys in `onSuccess` and uses
   `onError: toastApiError`.
5. **New error code** coming from the backend → register it in `API_ERROR_CODES` (`shared/types.ts`) and
   map the message in `shared/lib/error-messages.ts`. Never compare `code` with a string literal.
6. **SSR route** that needs this data → `server.ts` with `createServerFn`, and the `loader` does
   `prefetchQuery` with the `accessToken` from the router context (see [auth.md](auth.md)).
7. `bun run test` and `biome check`.
8. **Update [`features-index.md`](features-index.md)** — it is the map that saves a grep next cycle.

## Checklist — new feature module

1. `src/features/<name>/` with `api.ts`, `hooks.ts`, `types.ts`, `components/` (and `server.ts` only if
   it needs SSR or the httpOnly cookie). `types.ts` is a barrel re-exporting aliases from
   `#/shared/api`; add any missing alias to `src/shared/api/types.ts` (and, if the API record is
   missing or wrong, fix the API and regenerate: `v1.json`, then `bun run openapi`).
2. A component shared by more than one feature does not live here — it goes to `src/components/`.
3. New route in `src/routes/` (file-based). An authenticated client-only route uses the `beforeLoad`
   guard; an authenticated SSR route uses `context.accessToken`. Choose the strategy and record it in
   the rendering table in [architecture.md](architecture.md).
4. The rest is the endpoint checklist above, per endpoint.
5. Update `features-index.md` and the rendering table in `architecture.md`.
