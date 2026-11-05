# `openapi/v1.json` — the API's surface, versioned

This file is **generated**, not hand-written, and it is versioned on purpose: it is the contract
`VidroFront` compiles against. `bun run openapi` turns it into
`VidroFront/src/shared/api/schema.d.ts`, and the front's request/response types are aliases over
that file. The `.github/workflows/api.yml` workflow regenerates it and fails if the diff is not
empty, so a route or a success body does not change without showing up in review; `front.yml`
does the same for `schema.d.ts`.

It exists because P0.1 in [`docs/TODO.md`](../../docs/TODO.md) was exactly that: **4 wrong routes**
in `docs/agents/features-index.md`, which nobody had a way to check. The generated types came
later, from the design in
[`docs/superpowers/specs/2026-10-19-openapi-generated-types-design.md`](../../docs/superpowers/specs/2026-10-19-openapi-generated-types-design.md).

## Regenerate

```bash
ASPNETCORE_ENVIRONMENT=Development dotnet build src/VidroApi.Api -p:GenerateOpenApiDocument=true
cd ../VidroFront && bun run openapi   # then the front's types
```

Two unusual things in that command, both out of necessity:

- **`-p:GenerateOpenApiDocument=true`** — generation sits behind a property
  (`VidroApi.Api.csproj`). The everyday `dotnet build` does not pay the price below.
- **`ASPNETCORE_ENVIRONMENT=Development`** — the generator (`dotnet-getdocument`) **boots the
  host** to read the routes, and `AddInfrastructure` builds the MinIO client right away, with the
  endpoint from config. The endpoint only exists in `appsettings.Development.json`; without it the
  boot blows up. Nothing is connected — the endpoint only needs to be a valid string.

For the same reason, `Program.cs` skips the migration when the process is the generator
(`GetDocument.Insider`): generating the contract must not depend on a running Postgres. The boot
still logs an `FTL ... IServerAddressesFeature is not present` at the end — the generator has no
server; the document is written anyway.

## What makes it a type source

Each piece lives in `src/VidroApi.Api/Extensions/` and is locked by
`tests/VidroApi.UnitTests/Contracts/OpenApiDocumentTests.cs`, which reads this committed file:

| | how |
|---|---|
| Success bodies | `.ProducesApiSuccess<Response>()` / `.ProducesNoContent()` on each route (`RouteHandlerBuilderExtensions`). The handlers stay on `IResult`. |
| One schema per slice | `CreateSchemaReferenceId` joins the nested type chain: `CreateVideo.Request` → `CreateVideoRequest`, `ApiResponse<GetVideo.Response>` → `GetVideoResponseApiResponse` (`OpenApiExtensions`). The default (bare type name) collapsed all sixteen `Request`s into one. |
| Enum values | A schema transformer writes the numeric values of the domain enums (`ReactionType` → `1, 2`), in bodies and in query parameters. |
| Numbers | The web JSON defaults read `"5"` as a number, so ASP.NET describes every number as `integer \| string`. The transformer drops the `string` alternative: the API writes numbers and the front sends them. |
| Required | `init` properties are never marked required by ASP.NET; the transformer marks every property required, because System.Text.Json always writes them. Nullability stays in the property's type (`null \| string`). |

## What this file does **not** cover

| | state |
|---|---|
| Routes, methods, path and query parameters | **43 of 43** |
| Success bodies (200/201) and empty 204s | **all `/v1` routes**; the two webhooks declare an empty 200 |
| Error bodies (4xx) | **not described.** Domain `{ code, message }` and the FluentValidation 400 `{ errors: [...] }` are a follow-up in `docs/TODO.md`. The front keeps `ApiError`/`ValidationError` hand-written. |
| Enum **names** | Not here: requests carry `0 \| 1 \| 2`, responses an `EnumValue { id, value }`. Names and numbers are pinned by the golden [`contracts/enums.json`](../../contracts/README.md). |
