# Patterns to import from other repos

Survey done on 2026-08-31 over four reference repositories, looking for a rule or tool that has
already proven itself there and is missing here.

| Reference repo | Where it lives | What is worth taking |
|---|---|---|
| `AutoAnimeDownloader` | `~/Projetos/AAD/AutoAnimeDownloader` | Go testing patterns, geometry tests in the front |
| `mamut-api` | `~/Projetos/Mammute/mamut-api` | `.golangci.yml`, custom AST checker, contract test |
| `MT` (payment microservices) | `~/Projetos/MT` | .NET conventions: logging, tests, anti-patterns |
| — | — | frontend: own suggestions, there is no reference repo |

## Starting point

**The documentation conventions have already been imported.** `docs/agents/` with
architecture/conventions/config/design-decisions/features-index, numbered decisions with anchors,
checklists at the end of `conventions.md`, the "bad code along the way" rule, the commit
convention (Portuguese at the time; English since 2026-10-12) — all of that is already in the three
services, and came from AAD and mamut-api. None of it needs redoing.

**What is missing is enforcement.** Every Vidro rule is prose in a `.md` the agent may or may not
read. AAD, mamut-api and MT turn rules into something the machine enforces: a linter, an AST
script, a test. That is where the gain is.

---

## Go — VidroProcessor

### ~~1. `.golangci.yml` (from mamut-api) — biggest single gain~~ ✅ *(2026-09-07)*

`VidroProcessor/.golangci.yml` exists, with 15 linters, and the **Lint** step of
`.github/workflows/processor.yml` replaced the separate `gofmt` and `go vet` steps.
It cost **74 findings** and one decision per finding (details in `TODO.md`, "Small mess").
Two deviations from mamut-api, both deliberate:

- ~~**`contextcheck` was left out**~~ ✅ *(enabled on 2026-09-09)*: its 16 findings were all the
  same real problem — `queue/` and `minio/` did not accept a `context` and used
  `context.Background()` inside, so the job's cancellation reached neither Redis nor MinIO. The
  signature refactor was done and the linter is on; 2 findings remain, both `go notifyWebhook`,
  detached on purpose and with a `//nolint` naming the reason. See
  `VidroProcessor/docs/agents/design-decisions.md` **#13**.
- **`gci` was left out**: it fights `gofumpt` over the same file (one undoes the other in
  `golangci-lint fmt`, and `run` never goes green). `goimports` with
  `local-prefixes: video-processor` gives the same three groups.

*(original text below)*

CI ran `go build`, `go vet`, `go test`. mamut-api has a ~250-line `.golangci.yml` where each
linter **is** a `conventions.md` rule that became a red build.

Direct mapping between a rule Vidro has already written and the linter that enforces it:

| Rule already written in the Processor | Linter |
|---|---|
| `fmt.Errorf("...: %w", err)`, never `%v` | `errorlint` |
| no `os.Getenv` outside `config/config.go` | `forbidigo` + `path-except` |
| no stdlib `log` in the worker (except `config.go`) | `depguard` denying `log` |
| every external call with ctx/timeout | `noctx`, `contextcheck`, `bodyclose` |
| a non-critical error is swallowed **on purpose** | `nilerr` (catches the `return nil` with a live err that was *not* on purpose) |
| "just leave a TODO" is a valid option of the root rule | `godox` with `FIXME`/`HACK`/`XXX` banned and **`TODO` allowed** |

`godox` configured this way is a deliberate choice that mamut-api documents inside its own yml: a
TODO marked on the code line does not drift like an external doc item; FIXME/HACK/XXX signal wrong
code that shipped as is, and that needs a justification.

**`depguard` to freeze the architectural boundary:** checked on 2026-08-31 that
`internal/processor/processor-steps/*.go` **imports nothing from minio/redis** — the rule "every
external call goes through a circuit breaker" is already true in fact. A `depguard` block denying
those imports in `processor-steps/**` turns an accidental fact into an invariant.

mamut-api formatters that come along: `gofumpt`, `goimports`, `gci` with
`prefix(video-processor)`.

> Do not import from AAD: its import order (internal first, then stdlib, then third-party) is
> non-standard and fights `gci`.

### 2. A custom AST checker when no linter covers it (`make check-ownership`, from mamut-api)

mamut-api has `scripts/checkownership` + a **mandatory** Makefile target in the post-change cycle,
because the bug class it guards (IDOR) has no linter. The generalizable pattern: **a rule that
hurts and fails silently gets a ~100-line AST script instead of one more doc paragraph.**

Two candidates in Vidro, both silent failures the docs themselves already admit:

- ~~`VidroProcessor/docs/agents/conventions.md`, pipeline step checklist, step 4:
  *"Register the step in **both** orchestrators [...]"*~~ **No longer needs AST**
  *(2026-09-07)*: the two orchestrators now read **a single list** (`nonCriticalSteps` in
  `processor.go`), so the divergence is no longer possible — and `orchestration_test.go` asserts
  the list is exactly the four steps, in order.
  Lesson for the other candidates: **before writing the checker, see whether the duplication it
  would guard can be deleted.**
- `VidroApi/docs/agents/conventions.md`, feature slice checklist, step 2: *"A wrong signature does
  not fail the build — the route simply never exists, and the symptom is a 404 in a test."*

mamut-api also documents its own checker's limitations in the script header (it is best-effort,
not proof), which avoids a false sense of coverage. The habit is worth copying.

### 3. AST contract test (`access_test.go`, from mamut-api) — the most relevant one here

mamut-api has a test that reads the `huma.go` files **by AST** and checks the declared access class
against a versioned `classeEsperada` table — editing the table is what makes the security change
show up in the diff. It reads AST and not the OpenAPI on purpose, because a route with
`Hidden: true` does not enter the spec.

The analogue here is direct: **both P0s in `TODO.md` were contract divergences between the Go
webhook and the C# handler**, and nobody saw them for months. The monorepo allows a test that reads
the payload struct in Go and the `Request` in C# and fails when the fields diverge. It is the piece
`docs/MONOREPO.md` promises.

> **Covered another way** *(2026-09-07)*: instead of AST, the webhook payload became a golden in
> `contracts/`, read by the worker's test and by the API's — it catches the same drift with less
> code. What remained as discipline only was the rest of the contract (queue name,
> `callback_url`, MinIO paths).
> *(2026-10-12)* The queue name and the job envelope (`callback_url`, `correlation_id`) joined it
> in `contracts/job-state.json`; only the MinIO paths are still discipline.

### 4. Typed fake, never a mock (from mamut-api)

mamut-api removed `testify/mock` from the whole repo and documents the concrete reason: a mock
matches arguments **by value at runtime**, so when an id's type changed from `uint` to `uuid.UUID`
the expectations stopped matching and the tests broke **silently**, with no compile error. A fake
with a typed field breaks at build time. Worth writing as a rule in all three services.

### 5. Never let background work outlive the test (from AAD)

AAD's `docs/agents/testing.md` has the section *"Never Let Background Work Outlive Its Test"*,
with an "entry point → how to wait" table and a description of the symptoms: `-race` blames an
innocent test, and the failure only shows under `-count=5`.

The Processor is a worker with a goroutine pool and integration tests with testcontainers — the
same risk class, and nothing is written about it.

### 6. An instrument test that never fails, only measures (from AAD)

`live_pack_measure_test.go`: gated by an environment variable, *"not a regression test — a
measuring instrument"*, it only prints. It is what produced the numbers behind two of that repo's
decisions.

P6 in `TODO.md` is exactly "FFmpeg pipeline performance (P-PERF1..4, P-OPT1)". An instrument test
gated by `VIDRO_LIVE_FFMPEG=1` that measures time per step on a real video is the tool to tackle
that P6 with numbers instead of guesses.

---

## .NET — VidroApi

### 7. The API's `conventions.md` says **nothing** about logging (from MT)

The Processor has a logging section with levels; the API has not a line. MT has the most mature
section of the four repos:

- A `[{Feature}]` prefix as the first placeholder, **always via `nameof(...)`** — never a
  hardcoded string, never concatenation with `+`, a single template.
- The exception is the **1st argument** of `LogError`, never `.ToString()` embedded in the
  template.
- `{Name}` for a scalar; `{@Name}` (destructuring) only when serializing a whole object.
- Levels with explicit semantics: `Critical` = terminal/unrecoverable (security, crypto,
  corruption); `Error` = a real exception or a blocking failure; `Warning` = a non-blocking
  anomaly; `Information` includes **expected business errors** (it does not become a Warning just
  for being a failure path); `Debug` = verbose or sensitive payload.
- Do not duplicate what the middleware already logs — a handler logs business decision points, not
  a generic "starting"/"finishing".

### 8. Mandatory `// Arrange` / `// Act` / `// Assert` (from MT)

A top-priority guideline in MT, in a repo whose general rule is "avoid comments as much as
possible" — the exception is deliberate. Zero cost, and the API's `conventions.md` already has a
tests section where it fits.

### 9. Never `.First()` / `.Single()` / `.Last()` without `OrDefault` on external data (from MT)

Prefer `FirstOrDefault(...)` and handle the `null`/`default` explicitly. Vidro does not have this
rule, and the Processor's webhook is exactly "external data".

### ~~10. Fail fast in the domain, anomaly + ACK at the inbound (from MT)~~ ✅ *(2026-09-07)*

Written in `VidroApi/docs/agents/conventions.md`, section *"Fail fast in the domain, record an
anomaly at a machine inbound"*, with the whole BUG-1 chain as justification (a 500 to a machine
with a finite retry budget = a message silently dropped) and step 5.1 in the feature slice
checklist, which forces the question *"is the caller a machine?"*.

*(original text below)*

MT's rule: in a domain constructor, `throw` on overflow (fail-fast); in a consumer/inbound path,
**record an anomaly and ACK instead of throwing** — do not poison the queue.

This is literally BUG-1 in `TODO.md`: `ArgumentException.ThrowIfNullOrWhiteSpace` in the
`VideoArtifacts` constructor + a webhook handler that blew up → 500 → the Processor gives up after
3 retries → job already acked, it does not go to the DLQ → video stuck in `Processing` forever. The
bug was fixed; the **rule that would prevent it again** was written nowhere.

### 11. Injected time, never read inside the method (from MT and mamut-api)

Vidro has `DateTimeOffset now` as the last parameter **of entity constructors**. MT extends it to
any method that depends on time (`now` as the first parameter), and mamut-api enforces the same in
Go with `forbidigo` banning `time.Now` and `uuid.New` outside the edge. Worth unifying as a rule for
all three services, in the root `CLAUDE.md`.

### 12. A section of named anti-patterns (from MT)

MT has *"Anti-patterns (do not copy, even if they appear in a good repo)"* — each item with the
place where the bad pattern appears and why not to copy it. Vidro documents only what is right.
Given that `TODO.md` admits messy areas, a "this exists in the repo and is not to be copied" section
keeps the agent from replicating legacy by imitation.

### 13. `.editorconfig` + `Directory.Build.props` — neither exists

`find VidroApi -name "*.props" -o -name ".editorconfig"` comes back empty. The API's
`conventions.md` cites `// ReSharper disable once ...` in several places, i.e. the analyzers are
assumed, but nothing is versioned or runs in CI. It is the same gap as golangci, on the .NET side:
`TreatWarningsAsErrors`, `EnableNETAnalyzers`, `AnalysisLevel` in a `Directory.Build.props` at the
root of `VidroApi/`.

### 14. Checklists as **skills**, not as a paragraph (from MT)

`msac/.claude/skills/` and `bank-slip/.claude/skills/` have `create-http-feature`,
`create-consumer-feature`, `create-integration-test`, `add-test-scenario`, `publish-event`.
Vidro has the same checklists — only buried at the end of `conventions.md`, depending on the agent
remembering to read them. Turning the three ("feature slice" in the API, "pipeline step" in the
Processor, "new endpoint" in the Front) into invocable skills is repackaging content that already
exists.

---

## Frontend — VidroFront

There is no reference repo; what follows is suggestion, with two concrete gaps found in the survey.

### ~~15. Nothing runs `tsc --noEmit` — neither a script nor CI~~ ✅ *(2026-09-07)*

`tsconfig.json` was already well configured (`strict`, `noUnusedLocals`, `noUnusedParameters`,
`noFallthroughCasesInSwitch`) and **nothing ran the compiler**. Now `bun run typecheck` exists and
`.github/workflows/front.yml` runs the step before test.

**The "one line each" estimate was wrong:** turning the compiler on revealed **23 pre-existing
errors** in 11 files, fixed in the same batch — including a real bug (`currentUser?.id` on a
`UserProfile` that only has `userId`, leaving `CommentList`'s `isOwner` always false). Details in
`TODO.md`, under "Small mess". Lesson for the items still open here: **turning on a checker counts
the errors it finds** — the real cost is the cleanup, not the configuration. See item 16 (biome),
which already knew this.

### ~~16. Lint off in CI because of 108 pre-existing errors~~ ✅ *(2026-09-07)*

The comment in `front.yml` is honest about it, and `TODO.md` already records it. Cheap path: run
`biome check --write` (cleans ~100), solve the rest by hand in one batch, turn the step on.
Until then, `biome ci --changed` already stops the debt from growing without requiring the full
cleanup first.

### 17. Geometry tests (from AAD) — the best front pattern of the four repos

AAD's `tests/smoke/layout.spec.ts` attacks a class a content test does not catch: **a broken
layout does not fail a normal test** — the element is still in the DOM and still "visible" to
Playwright, it is just off screen. It asserts geometry: `scrollWidth <= clientWidth` at several
widths, `elementFromPoint` to check stacking (`z-index` under `position: sticky`), line count via
`range.getClientRects().length`.

Two lessons that hold even without copying the file:

- **A width test must run in the language with the longest labels.** Measured in AAD: the same
  bar is 358px in `en` and 438px in pt-BR. Those 80px are the difference between a bug that only
  reproduces below 320px and one that reproduces at 414px, on a real phone.
- **A layout test needs volume in its fixtures** (AAD uses 24 torrents, 30 animes) — with a
  two-row fixture nothing overflows and every assertion passes empty.

Vidro has a player, a feed grid and playlists: same risk. Today the 5 files in
`VidroFront/src/tests/` cover only `api-client`, auth, users and videos. AAD's layered structure
(`tests/unit/`, `tests/component/`, `tests/smoke/`) is the target.

### ~~18. `noUncheckedIndexedAccess: true`~~ ✅ *(2026-09-07)*

Enabled in `tsconfig.json`. It cost 4 fixes, **all in tests** — the production code already
handled indexing with `?? undefined`/`?.`, so the risk predicted here ("the biggest source of
runtime `undefined` left") did not materialize: the discipline already existed, it just was not
locked. Now it is.

---

## Suggested order

1. ✅ ~~**`.golangci.yml` in the Processor**~~ (item 1) — done on 2026-09-07 (74 findings, all
   resolved; `gci` was left out with a reason, and `contextcheck` came in on 2026-09-09)
2. ✅ ~~**Fail-fast vs. anomaly+ACK rule written**~~ (item 10) — done on 2026-09-07
3. ✅ ~~**`typecheck` in the front**~~ (item 15) — done on 2026-09-07 (it cost 23 fixes, not one
   line)
4. **Logging section in the API's `conventions.md`** (item 7) — the only section missing compared
   to the other two services
5. ✅ ~~**Go↔C# contract test by AST**~~ (item 3) — covered by the webhook golden in `contracts/`
   (2026-09-07), without AST, and by the job envelope golden (2026-10-12)

The rest (items 4, 5, 6, 8, 9, 11, 12, 13, 14, 17) has no dependency between items and can come in
as the corresponding area is touched. **Closed:** 1, 3, 10, 15, 16, 18. Item 2 lost half its scope:
one of the two AST checker candidates ceased to exist.

---

## Already resolved

- **2026-10-12** — item 3 extended: the queue name and the job envelope are locked by
  `contracts/job-state.json`, tested from both sides.
- **2026-09-09** — `contextcheck` enabled in the Processor: `queue/` and `minio/` now receive
  `context.Context`, and a cancelled job (budget exceeded or `SIGTERM`) now aborts in-flight I/O.
  See design-decisions #13.
- **2026-09-07** — item 1 (`.golangci.yml`) enabled in the Processor, with `contextcheck` and `gci`
  left out for documented reasons; half of item 2 (AST checker for the two orchestrators) became
  unnecessary — both now read a single list.
- **2026-09-07** — item 18 (`noUncheckedIndexedAccess`) enabled; item 16 (biome) closed, with the
  formatter on and the root ternary rule relaxed to allow one line.
- **2026-09-07** — item 10 (fail-fast vs. anomaly+ACK) written in the API's `conventions.md`.
- **2026-09-07** — item 15 (`typecheck` in the front) closed; see the item's section, rewritten
  with the real cost.

- **2026-08-31** — `VidroFront/docs/agents/conventions.md` said *"Forms: direct composition with
  shadcn components + `useState` + inline validation. No `react-hook-form` or form libs without
  discussion"*, while the repo's 8 forms use `react-hook-form` + `zodResolver` + shadcn's `Form*`
  components. Doc fixed to describe the real pattern. ("Docs that lie" category of `TODO.md`.)
