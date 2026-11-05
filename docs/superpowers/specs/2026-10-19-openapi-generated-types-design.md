# OpenAPI-generated front types

**Date:** 2026-10-19
**Status:** implemented *(2026-11-05)* — with two extra schema transformers (numbers without the `string` alternative, every property required); see `VidroApi/docs/agents/design-decisions.md` #15
**Closes:** P2 “Generate the front's types from the API's OpenAPI” in `docs/TODO.md` (the remaining gates)

The front stops hand-writing request/response shapes. The API declares success schemas in the versioned OpenAPI document; `openapi-typescript` generates a `.d.ts`; a thin alias layer keeps the names the UI already uses. Error responses stay out of this work.

---

## Goal

A change to a success request or response record in C# becomes a TypeScript error in the front (or a red CI job if the generated files were not updated) in the same commit. The six enum *numeric* mappings stay pinned by `contracts/enums.json`.

## Current state

- `VidroApi/openapi/v1.json` is generated at build (`-p:GenerateOpenApiDocument=true`) and diffed in `.github/workflows/api.yml`. It locks 43 routes and no response bodies: handlers return untyped `IResult`.
- Sixteen nested `Request` records collapse to a single schema named `Request`.
- `VideoVisibility` and `CommentSortOrder` appear as `{ "type": "integer" }` with no values; the other four domain enums do not appear.
- `contracts/enums.json` pins the six enums; both sides test it.
- `VidroFront/src/features/*/types.ts` retypes each request/response by hand. `apiClient` unwraps `{ data: T }` and returns `T`. Response enums are `EnumValue { id, value }`; request enums are the numeric id.

## Decisions

1. **Front consumes the generated types** (not “document only”). Hand-written shapes in `features/*/types.ts` become re-exports.
2. **Success only.** 200/201/204. `ApiError` and `ValidationError` stay hand-written. Domain 4xx and FluentValidation 400 are a follow-up (see below).
3. **`.Produces` on the route**, not `TypedResults` and not a reflection transformer. `ToApiResult` and the handlers stay on `IResult`. The contract is visible next to `MapGet`/`MapPost`.
4. **Schema IDs from the nested type chain**, not by renaming `Request`/`Response` in C#. Vertical slices keep those names.
5. **Friendly aliases** (`Video`, `VideoSummary`, …) so components do not import `components['schemas']['GetVideoResponse']`.
6. **`enums.json` stays.** Response enums are `EnumValue`, so OpenAPI never pins `VideoStatus = 2`. The named consts (`ReactionType.Like = 1`) stay in `shared/types.ts` because OpenAPI request enums are `1 | 2`, not `Like`.
7. **Webhooks stay in the document** (`Produces(200)` with no body) and stay out of the front alias surface.
8. **The generated `.d.ts` is committed.** CI regenerates and fails on diff. Same pattern as `v1.json`.

---

## Architecture

```
C# Request/Response records
        │  .Produces<ApiResponse<T>>(status) on Map*
        ▼
GetDocument.Insider  →  VidroApi/openapi/v1.json     (already CI-gated)
        │  unique schema ids + enum values
        ▼
openapi-typescript   →  VidroFront/src/shared/api/schema.d.ts
        │
        ▼
src/shared/api/types.ts   aliases (unwrap { data: T }, old UI names)
        │
        ▼
features/*/types.ts       export type { Video, … } from '#/shared/api'
        │
        ▼
apiClient.get<Video>(path)   still unwraps the envelope at runtime
```

Source of truth is the C# records. `v1.json` is the photocopy the front compiles against.

---

## API

### Route helper

One extension used by every feature, next to `ToApiResult`:

```csharp
public static RouteHandlerBuilder ProducesApiSuccess<T>(
    this RouteHandlerBuilder builder,
    int statusCode = StatusCodes.Status200OK) =>
    builder.Produces<ApiResponse<T>>(statusCode, "application/json");

public static RouteHandlerBuilder ProducesNoContent(this RouteHandlerBuilder builder) =>
    builder.Produces(StatusCodes.Status204NoContent);
```

Rule, matching what `ToApiResult` already returns:

| Handler result | Annotation |
|---|---|
| `ToApiResult(StatusCodes.Status200OK)` | `.ProducesApiSuccess<Response>()` |
| `ToApiResult(StatusCodes.Status201Created)` | `.ProducesApiSuccess<Response>(StatusCodes.Status201Created)` |
| `ToApiResult()` (`UnitResult` → 204) | `.ProducesNoContent()` |
| Webhooks (`Results.Ok()` empty 200) | `.Produces(StatusCodes.Status200OK)` |

Do not annotate 4xx. `RequireAuthorization()` stays as it is.

`ListPlaylistsByUser` / `ListPlaylistsByChannel` return `ApiResponse<PagedResult<PlaylistResponse>>` — `ProducesApiSuccess<PagedResult<PlaylistResponse>>()`.

### Schema reference ids

`builder.Services.AddOpenApi(options => { options.CreateSchemaReferenceId = ... })`.

Walk the declaring-type chain, then the generic argument:

- Nested CLR type: join names from the outermost type inward, no separator. `CreateVideo.Request` → `CreateVideoRequest`. `ListTrendingVideos.Response.VideoSummary` → `ListTrendingVideosResponseVideoSummary`. `GetVideo.Response` → `GetVideoResponse`.
- `ApiResponse<T>` → `{SchemaId(T)}ApiResponse` (example: `GetVideoResponseApiResponse`).
- `PagedResult<T>` → `{SchemaId(T)}PagedResult`.
- Domain enums (`VidroApi.Domain.Enums`) and `EnumValue` keep their type name (`ReactionType`, `EnumValue`).
- Anything else: `JsonTypeInfo.Type.Name` with arity suffix stripped.

The function must never return `Request` or `Response` alone. Default ASP.NET behaviour (`type.Name`) is what caused the collision.

### Enum schema transformer

`options.AddSchemaTransformer(...)`. When the CLR type is an enum under `VidroApi.Domain.Enums`:

- schema type is integer
- `enum` is the numeric values (`ReactionType` → `1`, `2`)

Applies to JSON bodies **and** query parameters (`CommentSortOrder sort` on list comments). Does not rewrite `EnumValue` (still `{ id: integer, value: string }`). Do not emit `x-enum-varnames` as a substitute for the hand-written consts.

### What does not change

`ResultExtensions.ToApiResult`, feature `Command`/`Handler`/`Validator` records, FluentValidation, `ExceptionMiddleware`, JSON serialization of `EnumValue`.

---

## Front

### Generated file

- Dev dependency: `openapi-typescript`, pinned.
- Script: `"openapi": "openapi-typescript ../VidroApi/openapi/v1.json -o src/shared/api/schema.d.ts"`
- Commit `src/shared/api/schema.d.ts`. Ignore it in `biome.json` the same way as `routeTree.gen.ts`: add `!**/src/shared/api/schema.d.ts` to `files.includes`.
- Do not boot the API to generate types. The committed `v1.json` is the input.

### Aliases

`VidroFront/src/shared/api/types.ts` maps today’s names onto `components['schemas']`. `src/shared/api/index.ts` re-exports that file so barrels import from `#/shared/api`.

`SignUp.Response` is `{ userId }` and is not an alias of `AuthTokens`. `RenewToken.Response` matches `SignIn.Response` field-for-field; call sites keep using `AuthTokens` (structural typing). User and channel avatar responses match field-for-field; both call sites use `UploadAvatarResponse`.

| Alias (today’s name) | Schema id (from the rule above) |
|---|---|
| `SignUpRequest` | `SignUpRequest` |
| `SignInRequest` | `SignInRequest` |
| `AuthTokens` | `SignInResponse` |
| `UserProfile` | `GetCurrentUserResponse` |
| `CreateChannelRequest` / `CreateChannelResponse` | `CreateChannelRequest` / `CreateChannelResponse` |
| `UpdateChannelRequest` | `UpdateChannelRequest` |
| `Channel` | `GetChannelResponse` |
| `ChannelSummary` | `ListUserChannelsResponseChannelSummary` |
| `ChannelsResponse` | `ListUserChannelsResponse` |
| `UploadAvatarResponse` | `UploadAvatarResponse` |
| `CreateVideoRequest` / `CreateVideoResponse` | `CreateVideoRequest` / `CreateVideoResponse` |
| `UpdateVideoRequest` / `UpdateVideoResponse` | `UpdateVideoRequest` / `UpdateVideoResponse` |
| `ThumbnailUploadResponse` | `UploadVideoThumbnailResponse` |
| `Video` | `GetVideoResponse` |
| `VideoSummary` | `ListTrendingVideosResponseVideoSummary` |
| `FeedPage` | `ListFeedVideosResponse` |
| `SearchPage` | `SearchVideosResponse` |
| `ChannelVideoSummary` | `ListChannelVideosResponseVideoSummary` |
| `ChannelVideosPage` | `ListChannelVideosResponse` |
| `TrendingResponse` | `ListTrendingVideosResponse` |
| `CommentSummary` / `CommentsPage` | `ListCommentsResponseCommentSummary` / `ListCommentsResponse` |
| `ReplySummary` / `RepliesPage` | `ListRepliesResponseReplySummary` / `ListRepliesResponse` |
| `Playlist` | `GetPlaylistResponse` |
| `PlaylistItem` | `GetPlaylistResponsePlaylistItemResponse` |
| `PlaylistSummary` | `ListPlaylistsByUserPlaylistResponse` |
| `PlaylistsPage` | `ListPlaylistsByUserPlaylistResponsePagedResult` |
| `CreatePlaylistRequest` / `CreatePlaylistResponse` | `CreatePlaylistRequest` / `CreatePlaylistResponse` |
| `UpdatePlaylistRequest` | `UpdatePlaylistRequest` |
| `EnumValue` | `EnumValue` |

If a feed/search `VideoSummary` schema is structurally different from trending’s, do not force the `VideoSummary` alias onto those calls: use the operation-specific schema in that `api.ts` function. Do not unify the C# records just to keep one name.

If a hand-written field disagrees with C# (example: playlist list item `id` vs `playlistId`), **the generated schema wins** and the front call sites are updated. That is the drift this item exists to catch.

### Feature `types.ts`

Each `features/<name>/types.ts` becomes a barrel:

```ts
export type {
  Video,
  VideoSummary,
  CreateVideoRequest,
  // ...
} from '#/shared/api'
```

(`#/shared/api` resolves through `src/shared/api/index.ts`.)

Components, hooks and tests keep importing from the feature. The module layout in `conventions.md` stays (`api.ts` / `hooks.ts` / `types.ts`); the shapes originate in OpenAPI.

### `shared/types.ts`

**Keep:** `ApiError`, `ValidationError`, `API_ERROR_CODES`, the six enum consts (`VideoStatus`, `VideoVisibility`, `ReactionType`, `PlaylistVisibility`, `PlaylistScope`, `CommentSortOrder`) and `ReactionTypeValue`.

**Move to generated aliases:** `EnumValue`.

**Delete if unused after the swap:** `ApiSuccess`, `CursorPage`, `PagedResult`.

`enum-contract.test.ts` still reads the consts against `contracts/enums.json`.

### Runtime

`apiClient` still unwraps `{ data: T }`. Call sites still pass the unwrapped type (`apiClient.get<Video>(...)`). 204 still returns `undefined`. Presigned MinIO PUTs are not API types.

---

## Tests

### API — `VidroApi/tests/VidroApi.UnitTests/Contracts/OpenApiDocumentTests.cs`

Reads the committed `openapi/v1.json` (no Docker, same pattern as `EnumContractTests`).

1. Every operation whose path starts with `/v1/` and whose success status is 200 or 201 has a response schema (`application/json`). 204 has no content body. Webhooks (`/webhooks/...`) are excluded from the “must have a body schema” assertion; they must still declare 200.
2. No schema in `components.schemas` is named exactly `Request` or exactly `Response`.
3. Each domain enum that appears in the document (`ReactionType`, `VideoVisibility`, `CommentSortOrder`, `PlaylistVisibility`, `PlaylistScope` — and `VideoStatus` if it ever appears as a raw enum) has `enum` values equal to `contracts/enums.json` for that name.

Mutations that must turn a test red:

- Remove `.ProducesApiSuccess` from `CreateVideo` → (1).
- `CreateSchemaReferenceId` returns `type.Name` → (2).
- Enum transformer skips `ReactionType` → (3).

Existing endpoint tests do not change. `api.yml` regenerate-and-diff stays.

### Front

- `bun run openapi` + `git diff --exit-code src/shared/api/schema.d.ts` in `front.yml`, after install, before lint.
- `typecheck` is the gate that the aliases match call sites.
- `enum-contract.test.ts` unchanged.
- Feature tests keep importing from `features/*/types`.

---

## CI

`.github/workflows/front.yml`:

- `paths` also includes `VidroApi/openapi/**`, so a commit that updates `v1.json` and forgets `schema.d.ts` fails the front job. Checkout is the whole repo; the path filter only decides whether the job runs.
- New step: `bun run openapi` then `git diff --exit-code` on the generated `.d.ts`.

`.github/workflows/api.yml` is unchanged aside from whatever the new unit tests need (they live under `VidroApi/tests`, already in the path filter).

Shared-contract rule: a change to a success record, `v1.json`, `schema.d.ts` and any alias that moves is **one commit**.

---

## Docs to update in the implementation batch

- `docs/TODO.md` — close the OpenAPI item; keep the error follow-up open; refresh “Closed so far” and “Suggested order”.
- `docs/MONOREPO.md` — the “API ↔ Front — generated types” bullet becomes done (errors still listed as follow-up).
- `VidroApi/openapi/README.md` — the “what this file does not cover” table: responses and request schema ids and enum values are done; 4xx still not covered.
- `VidroApi/docs/agents/design-decisions.md` #11 — document now describes success bodies; point at this spec’s follow-up for errors. New numbered entry if the `.Produces` + schema-id + transformer trio is a *why* that does not fit in #11.
- `VidroApi/docs/agents/conventions.md` — adding a feature slice includes `.ProducesApiSuccess` / `.ProducesNoContent` on the route.
- `VidroFront/docs/agents/architecture.md` — generated types; translate remaining Portuguese on contact.
- `VidroFront/docs/agents/conventions.md` — `types.ts` is a barrel over `#/shared/api`; request enums still numeric ids, response enums still `EnumValue`; translate Portuguese on contact.
- `VidroFront/docs/agents/features-index.md` — types row points at the generated aliases.
- `contracts/README.md` — `enums.json` remains the numeric pin; request/response shapes are `openapi/v1.json` + the front `.d.ts`.

---

## Follow-up (not this spec)

Recorded in `docs/TODO.md` as well.

1. **Domain errors.** A global OpenAPI document/schema transformer adds `{ code, message }` (`ApiErrorResponse`) on 400/401/403/404/409. One file, not 43 `.Produces`. The `apiClient` already throws `ApiClientError`; the gain is a honest catalog.
2. **FluentValidation 400.** Middleware returns `{ errors: [{ field, message }] }`. Domain `ErrorType.Validation` returns `{ code, message }` on the same status. OpenAPI cannot describe both without `oneOf`, a lie, or unifying the JSON (an API contract change). Do this only when a form needs to paint `errors[].field` on inputs — today `ValidationError` is unused in app code and `apiClient` casts every error as `ApiError`.

---

## Out of scope

- Rewriting `ToApiResult` to `TypedResults`.
- Unifying the two 400 bodies.
- Generating `API_ERROR_CODES` or the six enum consts from OpenAPI.
- Replacing `EnumValue` in responses with a raw integer.
- Sharing C# nested `VideoSummary` records across list endpoints.
- Typing presigned MinIO uploads.
- E2E upload → play.
- tRPC / a same-language shared package.

---

## Success criteria

- Regenerating `v1.json` produces response schemas for every `/v1` 200/201, empty 204, unique schema names, and integer enums with values.
- `OpenApiDocumentTests` fail under the three mutations above.
- `bun run openapi` is a no-op when `schema.d.ts` is current.
- `bun run typecheck` and the existing front tests pass with `features/*/types.ts` as barrels.
- A renamed C# response property (example: `GetVideo.Response.Title` → `Headline`) updates `v1.json` and `schema.d.ts` in the same commit, or CI is red.
- No 4xx schema is required for the item to close.

## Implementation shape

Several commits (API metadata → regenerate `v1.json` → front generate + swap → docs). Branch vs `master` is asked when the implementation plan is written; do not decide it in this spec.
