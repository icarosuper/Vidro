# CLAUDE.md

File give guidance to Claude Code (claude.ai/code) when work with code in repo.

Commit convention, branching, edit scope, code language, release/tags and the "bad code nearby" rule
live **once**, in the monorepo root `../CLAUDE.md` (loaded automatically alongside this file). Short
version: all code in English, conventional commits **in Portuguese**, subject line only; straight to
`master` unless the user approves a branch; **never commit without an explicit request**.

## Keeping the docs healthy

The post-implementation cycle (tests → docs → suggested commit) is in the root `../CLAUDE.md`. What
is specific here is **which** file to update:

- Schema, endpoint or feature added/removed → `docs/agents/features-index.md` (+ `docs/plans/`)
- New setting in `appsettings.json` or a `Settings` POCO → `docs/agents/config.md`
- New non-obvious pattern, or a *why* that changed → new numbered entry in `docs/agents/design-decisions.md`
- Layer boundaries or cross-cutting flow changed → `docs/agents/architecture.md`
- Coding rule changed → `docs/agents/conventions.md`

Tests for this service: `dotnet test`.

## Commands

```bash
# Build
dotnet build

# Run API (development)
dotnet run --project src/VidroApi.Api

# All tests
dotnet test

# Single test class
dotnet test tests/VidroApi.UnitTests --filter "FullyQualifiedName~ClassName"

# Single test method
dotnet test tests/VidroApi.UnitTests --filter "FullyQualifiedName~ClassName.MethodName"

# EF Core migrations (always specify both projects)
# Migration names must follow the pattern: PascalCaseDescriptionMigration (e.g. AddCommentsFeatureMigration, ChangeUsernameMaxLengthMigration)
dotnet ef migrations add <DescriptionMigration> --project src/VidroApi.Infrastructure --startup-project src/VidroApi.Api --output-dir Persistence/Migrations
dotnet ef database update --project src/VidroApi.Infrastructure --startup-project src/VidroApi.Api

# Start dependencies (compose lives in the parent dir, alongside the three repos)
docker compose -f ../docker-compose.yml up -d postgres redis minio

# Or the whole stack — API, front, processor, observability
cd .. && docker compose up -d --build
```

## Architecture overview

```
Domain ← Application ← Infrastructure ← Api
```

Each feature is self-contained file under `src/VidroApi.Api/Features/<Domain>/FeatureName.cs`.

→ Read `docs/agents/architecture.md` when create new feature, endpoint, or add auth/VideoProcessor integration.

## Conventions

→ Read `docs/agents/conventions.md` before create or edit domain entities, write features, or add tests. Carries the step-by-step checklists for **adding a feature slice** and **adding a setting** — follow them instead of copying a neighbouring file by eye.

## Design decisions

→ Read `docs/agents/design-decisions.md` when implement deletion, counters, pagination, cascades, or MinIO cleanup. Numbered entries with an index at the top — **read only the entry you were pointed at**, and cite it by anchor (`design-decisions.md #7`), never by line number.

## Config reference

→ Read `docs/agents/config.md` when touch `appsettings.json`, a `Settings` POCO, or an env var. Every key with its default, where it is read, and why the default is what it is.

## Features index

→ Read `docs/agents/features-index.md` to locate existing feature file before search codebase. Update whenever feature added or removed.

## Implementation plan

See `docs/plans/2026-03-26-implementation-plan.md` for full task-by-task plan.
