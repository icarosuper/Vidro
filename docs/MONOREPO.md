# Monorepo — why, how, and what it cost

Record of the migration from three repositories (`VidroApi`, `VidroFront`, `VidroProcessor`) to
one, done on **Aug 30, 2026**. Read it before proposing to go back, adding a monorepo tool, or
touching CI.

Paths cited here are relative to the **monorepo root**, not to this folder.

---

## Findings that motivated the change

None of them is an architecture opinion — all of them showed up in the repository itself.

1. **Three shared files lived outside any repo.** `docker-compose.yml` (what makes the stack
   exist), `TODO.md` and `CLAUDE.md` sat loose in the folder that held the three clones, with no
   versioning at all. The monorepo was already being born; it only lacked the `git init`.

2. **The docs already referenced each other across services by relative path.**
   `VidroFront/CLAUDE.md` says to read `../VidroApi/docs/agents/features-index.md`; the API points
   at `../../../VidroProcessor/docs/agents/troubleshooting-stuck-video.md`. Those links only worked
   because the three clones sat side by side with those exact names — a monorepo assumption without
   a monorepo's guarantee.

3. **The P0s in `TODO.md` are all contract divergences between services**, and the text itself
   says "found in this analysis, **not recorded anywhere**". BUG-1: the Processor documented that
   the success webhook may arrive without an optional artifact, the API did not honor it, and every
   video in that condition stayed stuck in `Processing` forever. In separate repos that is an
   archaeological find; in an atomic commit it is a red CI.

4. **The convention already said to treat them as one.** "When changing the shared contract, both
   repos must be tagged and deployed together" was written in two `CLAUDE.md` files. It was manual
   discipline for a problem the structure solves.

5. **A single developer.** None of the real reasons for separate repos applies: no per-team
   permissions, no independent release cadence, no different owner per service.

---

## Decisions

### 1. Monorepo, without a monorepo tool

No Turborepo, Nx, moon or Bazel. **Turborepo is a JS-ecosystem tool** and orchestrates
`package.json` scripts over workspaces — here there is **one** JS package across three services.
The other two would only get in through a façade `package.json` calling `dotnet build` /
`go build`.

Turborepo's three gains do not exist in this project:

- **Dependency graph between packages** — there is none. The front does not import the API at
  build time; it talks over HTTP at runtime. There is no topological order to resolve.
- **Task cache** — each toolchain already has its own, and a better one: `dotnet`'s incremental
  build, Go's build cache, Vite/bun in the front. Turbo would cache on top of them.
- **Team remote cache** — a single developer.

The truly polyglot ones (Nx, moon, Bazel) charge up-front configuration that only pays off with
dozens of packages, parallel teams or a painful build. Three services with fast builds come nowhere
near.

**Trigger to reconsider:** when there is a **second** JS package — a `packages/api-types` generated
from OpenAPI, consumed by the front and by an eventual admin. Even then the first answer is **bun
workspaces**, which is already installed and free. Turborepo only if, after that, the build hurts.

### 2. `git subtree add`, not `filter-repo`

History preserved with plain git, no external tool:

```bash
git subtree add --prefix=VidroApi ../vidro-legacy/VidroApi master
```

The 121 commits of the three histories are in the repository, and `git blame` attributes each line
to the original commit (including across the historical `VidaroApi` → `VidroApi` rename).

The alternative, `git filter-repo --to-subdirectory-filter`, would rewrite the historical paths and
make `git log -- VidroApi/` perfect, at the cost of rewriting every SHA and depending on a tool
outside git. See tradeoff #1 below.

### 3. CI: one workflow per service, with the native path filter

`.github/workflows/{api,front,processor}.yml`, each with `on.push.paths` / `on.pull_request.paths`
pointing at its own directory and at itself. A commit in the front does not trigger a .NET build.

**Why not a single workflow with an `if:` per job:** `on:` belongs to the workflow, not the job, so
a single file would need a third-party action (`dorny/paths-filter`) to decide what to run. Three
files with the native filter deliver the same with no dependency.

The three old `.github/workflows/ci.yml` were deleted: GitHub only reads `.github/workflows/` at
the root, so inside the subdirectories they were dead files pointing at a world that no longer
exists.

### 4. The old clones became a backup, not trash

`../vidro-legacy/{VidroApi,VidroFront,VidroProcessor}` are the three original repositories, intact,
with their `origin` on GitHub. They are the subtree source and the rollback path. **Do not delete
them before confirming the monorepo is on GitHub and nobody needs the old remotes.**

---

## Accepted tradeoffs

### 1. `git log -- <service>/` only sees post-migration history

A direct consequence of `subtree add`: old commits carry paths without the prefix
(`src/VidroApi.Api/Program.cs`, not `VidroApi/src/...`), and `--follow` does not cross the subtree
merge.

What **keeps working**: the full `git log`, `git show <sha>`, and above all `git blame`, which
attributes lines to the original commits. In practice what is lost is the directory-filtered log
for pre-migration commits.

Way out, if it ever hurts: redo the import with `git filter-repo --to-subdirectory-filter
<service>` in each clone before merging. It rewrites SHAs, so it is only worth it before the
monorepo has relevant history of its own.

### 2. One tag versions all three services

`v1.2.0` now covers API, front and worker together — the worker gets a version when only the front
changed.

Accepted because the three run on the same `docker-compose.yml` and the convention already
required a coordinated deploy on contract changes. The gain outweighs the noise: **there is never a
combination of versions that was not tested together.**

Whoever needs independent versioning later: prefixed tags (`api/v1.2.0`) solve it without leaving
the monorepo.

### 3. Larger checkout

One clone brings .NET + Go + TypeScript. Irrelevant at this scale; it becomes a problem when the
history gets heavy, and then the answer is `git clone --filter=blob:none`, not going back to three
repos.

### 4. Path filter × branch protection

A job skipped by the path filter shows as "skipped", not "success". If there is ever branch
protection requiring those checks, a PR that only touches the front would be stuck waiting for the
API check that will never run. Today there is no branch protection (commits go straight to
`master`), so it does not bite. If you turn it on: use an `all-green` job that always runs and
depends on the others with `if: always()`.

---

## What the migration unlocks (and is not done yet)

The biggest gain is not build, it is **contract**. Two things that were manual and already cost
bugs:

- **API ↔ Front — generated types.** ✅ *(2026-09-09, partial)* The front's `types.ts` mirrors the
  backend shapes **by hand**. The six enums became a golden in
  [`contracts/enums.json`](../contracts/README.md), tested from both sides, and the 43 routes are
  locked by the versioned `VidroApi/openapi/v1.json`, which CI regenerates and compares.
  **Missing:** generating the request/response types: the OpenAPI document does not describe the
  responses yet (the handlers return `IResult`) — see the item in P2 of `TODO.md`.
- **API ↔ Processor — contract test.** ✅ *(2026-09-07, partial)* The `video-processed` webhook
  payload became a versioned golden in [`contracts/`](../contracts/README.md), tested from both
  sides: the worker proves it serializes exactly that, the API proves it accepts exactly that. It
  is the net that would have caught BUG-1 on the spot. *(2026-10-12)* The job the API publishes is
  locked too: `contracts/job-state.json` pins the queue name (`JobQueueSettings:QueueName` ↔
  `PROCESSING_REQUEST_QUEUE`), the `job:<id>` key and the envelope (`callback_url`,
  `correlation_id`). **Still missing:** the MinIO path layout, which is only documented.

Neither needs a monorepo tool. They needed the monorepo.
