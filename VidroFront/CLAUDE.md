# CLAUDE.md

Navigation guide for Claude Code in this repo. Critical rules are here; the rest lives in `docs/agents/`.

Commit convention, branching, edit scope, language, release/tags and the "bad code nearby" rule
live **once**, in the monorepo root `../CLAUDE.md` (loaded automatically alongside this file). Short
version: everything in English (code, docs, file names, commits), conventional commits, subject
line only; straight to `master` unless the user approves a branch; **never commit without an
explicit request**.

## Project

Frontend rewrite of Vidro (a video platform). The .NET backend in `../VidroApi/` is complete. The frontend is built incrementally, phase by phase.

## Always-on rules

- **API endpoints:** before implementing an endpoint in the frontend, **read `../VidroApi/docs/agents/features-index.md`** to confirm the exact path and the backend source file (`src/VidroApi.Api/Features/...`). Do not guess paths. The request/response shapes come from the generated types (`#/shared/api`, built from the API's OpenAPI document) — do not hand-write them.
- **No direct `fetch`:** all HTTP goes through `apiClient` (`src/shared/lib/api-client.ts`). Exceptions: presigned uploads and `features/*/server.ts`.

## Commands

```bash
bun run dev                                 # http://localhost:3000
bun run build
bun run typecheck                           # tsc --noEmit (runs in CI)
bun run test                                # all
bun run test src/tests/api-client.test.ts   # one file
bun run lint                                # biome ci (runs in CI)
biome check                                 # the same, with output for iterating locally
bun run openapi                             # regenerates src/shared/api/schema.d.ts from ../VidroApi/openapi/v1.json (CI fails if stale)
```

**The formatter decides formatting** — `bun run lint` (`biome ci`) runs in CI and fails on any
file that does not follow the standard (2 spaces, single quotes, no semicolons). It collapses a
short ternary onto one line, and the root rule was adjusted to allow that; do not try to keep a
short ternary on three lines, the formatter undoes it.

Three Biome traps that cost time:

- A `//` comment inside `biome.json` **silently breaks the config** — it falls back to the default
  and starts linting `dist/` (83 → 148 files), with no error at all.
- `biome-ignore` only works with the reason on **one line**; split across two it becomes an
  "unused suppression" and the rule keeps firing.
- A suppression applies only to the **next line**. In multi-line JSX it goes **inside the
  tag**, right next to the attribute (`key={i}`) — if it sits before the `<div`, the formatter
  moves the attribute and the suppression comes loose.

## Stack summary

TanStack Start (selective SSR, file-based routing) · TanStack Query · shadcn/ui + Tailwind v4 · bun · Biome · Vitest

`VITE_API_URL` → .NET backend (default `http://localhost:5000`)

## What to read when

Read the doc **before** the task it describes. Skipping it → breaking patterns that exist for a reason.

- **[docs/agents/architecture.md](docs/agents/architecture.md)** — Before creating routes, touching layers, `apiClient`, shared types or root providers. Covers the `src/` layout, responsibilities, the API client rule, generated API types, rendering strategy, API response formats, query keys.

- **[docs/agents/conventions.md](docs/agents/conventions.md)** — Before writing any code: naming, feature module layout, `api.ts`/`hooks.ts` patterns, error handling, imports. (Readability — named variables, ternary formatting, function extraction — is in the root `../CLAUDE.md` and applies to all three services.) It also holds the **new endpoint checklist** and the **new feature module checklist** — follow the checklist instead of eyeballing a neighbor and copying it.

- **[docs/agents/auth.md](docs/agents/auth.md)** — Before touching sign in/up/out, route protection, `tokenStore`, `renewToken`, or anything about SSR hydration. Covers the sign-out ordering rule and the server snapshot pattern in `useIsAuthenticated`.

- **[docs/agents/features-index.md](docs/agents/features-index.md)** — Before creating/touching a feature, or when looking for "where is endpoint X / hook Y". Maps each feature: files, backend endpoints, hooks, types, components. **Update it when adding/removing endpoints, hooks or components.**

## Keeping the docs up to date

The post-implementation cycle (tests → docs → suggested commit) is in the root `../CLAUDE.md`. What
is specific to this service is **which** file to update:

- Added/removed an endpoint, hook or component → `docs/agents/features-index.md`
- Changed layers, `apiClient` or the rendering strategy → `docs/agents/architecture.md`
- Any auth change → `docs/agents/auth.md`
- Changed a code convention (rare) → `docs/agents/conventions.md`
