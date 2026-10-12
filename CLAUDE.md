# CLAUDE.md — Vidro monorepo

Rules that apply to **all three services**. Each service has its own `CLAUDE.md` with what is
specific to it; what is here is not repeated there.

## The three services

| Service | What it is | Agent docs |
|---|---|---|
| `VidroApi/` | .NET API (Clean + Vertical Slice), Postgres, MinIO, Redis | `VidroApi/docs/agents/` |
| `VidroFront/` | TanStack Start + React, consumes the API | `VidroFront/docs/agents/` |
| `VidroProcessor/` | Go worker: Redis queue → FFmpeg pipeline → MinIO → webhook | `VidroProcessor/docs/agents/` |

The three are **a single repository** — this one. The root holds the whole stack's
`docker-compose.yml`, `TODO.md`, this file and `docs/MONOREPO.md` (why it is a monorepo, and what
that cost).

**Edit scope:** the default is still to touch **one** service per task — read the other two freely
to understand contracts, types and endpoints. The exception is the shared contract: then both sides
change together, in the same commit, and that is the right behavior, not scope creep. Outside that
case, if something is missing in another service, tell the user before touching it.

**Shared contract** (Redis queue names, the job envelope, MinIO paths, the webhook shape): change
both sides **in the same commit**. The `video-processed` webhook payload and the job the API
publishes (queue name, `job:<id>` key, envelope fields) are locked by test: the goldens in
[`contracts/`](contracts/README.md) are read by the worker and by the API, so changing a field
means editing the golden and both sides together. Only the MinIO path layout is still discipline.
It is the main reason this is a monorepo — both P0s in `TODO.md` were contract divergences nobody
saw for months. See `VidroProcessor/docs/agents/design-decisions.md`.

## Language: English only

**Everything in English**, in all three services and at the root: code (class, method, variable,
type, test, log message, comment, XML doc), docs, file and directory names, commit messages, PR
titles. No exceptions. (The UI was already English — `<html lang="en">`.)

Some older text is still in Portuguese — mostly the `VidroFront` docs. It gets translated **on
contact**, not all at once: whenever you edit or pass through a file and find Portuguese in it,
translate it to English as part of the task and **tell the user** what you translated. This is an
exception to "Bad code along the way" below — no need to ask first, just report it.
Renaming a file means fixing every link to it in the same change (`grep` for the old name).

Not translated: test data whose point is the content itself (e.g. `"joão"` testing accents).

## Readability

These apply to **all three** services — C#, TypeScript and Go. What is idiomatic to one language
lives in that service's `conventions.md`; what is here is not repeated there.

- **Named variable instead of an inline expression.** The result of a check, query or async call
  gets a name before it enters a condition. Never inline inside the `if`.

  ```ts
  // ✅
  const emailAlreadyTaken = users.some((u) => u.email === email)
  if (emailAlreadyTaken) return { error: 'Email already in use' }

  // ❌
  if (users.some((u) => u.email === email)) return { error: 'Email already in use' }
  ```

- **Complex logic becomes a named function.** If a block needs a comment to explain itself, it
  needs a name.

- **A name says "what", not "how".** No abbreviations, no single-letter names (except loop
  indices), no generic names — `result`, `data`, `temp`.

- **A short ternary fits on one line; a long or nested one goes on three** — condition, `?`, `:`.
  Until 2026-09-07 the rule was "always three lines, never one"; it was dropped because the Biome
  formatter (`VidroFront`) collapses short ternaries and has no option to preserve them — keeping
  the rule meant turning off the formatter for the whole front. In the front **the formatter
  decides**: run `bun run lint`. In C# and Go, use judgment — the criterion is the same one the
  formatter applies: break when it does not fit on the line.

  ```ts
  // ✅ short
  const label = isAuthenticated ? 'Sign out' : 'Sign in'

  // ✅ long or nested — three lines, and consider extracting a named function
  const nextMode = mode === 'light'
    ? 'dark'
    : resolveModeFromSystemPreference(mode)
  ```

## Video stuck in `Processing`

The runbook is [`docs/troubleshooting-stuck-video.md`](docs/troubleshooting-stuck-video.md). It
lives at the root because it crosses API → Redis → worker → MinIO; no single service resolves it.

## Commit message convention

Conventional Commits, **in English**, subject line only — no body, no scope, no footer (no
`Co-authored-by`).

Format: `<type>: <imperative verb> <complement>` — lowercase after the type, no trailing period,
up to ~72 chars.

Types used in the repo (by real frequency): `feat` > `chore` > `fix` > `refactor` > `docs` / `test`.

- `feat` — new or extended functionality
- `fix` — bug/behavior fix
- `chore` — docs, README, migrations, scaffold, reorganization without logic
- `refactor` — rename/restructure without changing behavior
- `docs` / `test` — when the change is only documentation or only tests

Examples: `feat: add channel avatar upload`, `fix: fix reactions button`, `chore: update README`,
`refactor: rename project`. (Commits before 2026-10-12 are in Portuguese; history is not rewritten.)

The subject states the **behavior**, not the file: describe the rule that now holds. If the diff
doesn't fit in one subject, it has become more than one commit.

PR title (squash merge): `Feature/branch-name (#N)`.

## Where to commit

- **Default: straight to `master`.** Small things and bugfixes do not open a branch.
- **Exception: a large feature** (several commits). Then **ask the user** whether to create
  `feature/<topic>` or go straight to `master` — never decide alone.
- `master` is always deployable; production ships from `vX.Y.Z` tags (intended strategy — there is
  no tag yet). One tag versions **all three services at once**: that is the price, and the
  advantage, of the monorepo — there is never a combination of versions that was not tested
  together. See `docs/MONOREPO.md`.
- **There is no deploy pipeline.** `.github/workflows/{api,front,processor}.yml` only build and
  test, one per service, with a path filter. Deploy is manual.

## Never commit without an explicit request

1. Implement the changes
2. Run the tests and check they pass
3. Show what changed and **suggest one** commit title (not a list of options)
4. Wait for the user to approve or ask for the commit

## Bad code along the way

Found bad code **near** what you are touching — badly written, deprecated, unused, outdated,
unoptimized, duplicated, a comment lying about the code — **ask before acting**. Always the three
options, and the user chooses:

1. **fix it now while you are here**
2. **just leave a TODO** (in the code, next to the snippet)
3. **ignore it**

Never pick one of the three on your own — neither "I'll fix it in passing because it's quick" nor
"I'll leave it alone because it's out of scope". Say what it is, where it is (`file:line`) and why
it is bad, in one or two lines, and ask. This applies to what you find in passing; what the user's
request already covers is not "in passing", it is the work.

## After any change

1. **Run the tests** of the service you touched (`dotnet test` / `bun run test` / `go test ./...`).
2. **Update the affected docs in the same batch** — each file's trigger is in the service's
   `CLAUDE.md`. An outdated doc costs more than missing code: this root's `TODO.md` has a whole
   section ("Docs that lie") that only existed because of that.
   The **root** docs have triggers too:
   - **`TODO.md`** — the task closes, advances or discovers an item: strike/update the item in the
     body **and** the "Closed so far" summary at the top **and** the "Suggested order" at the end.
     All three together — striking only the body is what left the suggested order pointing at
     work already done.
   - **`docs/observability.md`, `docs/patterns-to-import.md`, `docs/MONOREPO.md`** — you closed a
     gap, step or item they list: mark it ✅ there too, including in the "Suggested order" and in
     the "What already exists" section when there is one.
   - **`README.md`** — a port, command, cross-service flow or root doc file changed.
   - Before suggesting the commit, `grep` for the item/file name in the root and service docs: a
     doc that still calls it "missing" or "next" is lying.
3. **Suggest the commit title** and stop.
