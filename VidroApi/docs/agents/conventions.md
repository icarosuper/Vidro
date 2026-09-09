# Conventions

## Domain entity conventions

- **Public constructor** w/ required fields + `DateTimeOffset now` as last param (passed via `: base(now)`). All biz-value props init in constructor body — incl defaults like `FollowerCount = 0` or `IsRevoked = false`. `= null!` on string/nav props = nullability annotation for EF Core parameterless ctor, not real init.
- **Parameterless constructor for EF Core** — always `private` (or `protected` for abstract base), decorated w/ `// ReSharper disable once UnusedMember.Local` and `[ExcludeFromCodeCoverage]`
- **`init` for immutable props** (e.g. `Username`, `CreatedAt`, `Id`); **`private set` for mutable** (e.g. `Email`, `PasswordHash`, counters). EF Core hydrates both via reflection — no `public set` needed.
- **Nullable props** (`string?`, `DateTimeOffset?`) never need `= null` — implicit default. Only `= null!` on non-nullable strings/navs (suppress CS8618 from EF Core parameterless ctor).
- **Navigation collections** — backing field w/ `// ReSharper disable once CollectionNeverUpdated.Local` to suppress IDE warning (EF Core populates via reflection):
  ```csharp
  // ReSharper disable once CollectionNeverUpdated.Local
  private readonly List<Video> _videos = [];
  public IReadOnlyList<Video> Videos => _videos.AsReadOnly();
  ```
- **Domain methods for state mutations** — e.g. `video.MarkAsReady(...)`, `user.ChangeEmail(...)`. Logic encapsulated, not spread across slices.
- **No counter methods on entities** — counters (`LikeCount`, `DislikeCount`, `ViewCount`, `FollowerCount`) updated atomically via `ExecuteUpdateAsync` in feature handler. Entity methods like `IncrementLikeCount()` not safe under concurrency — must not be added.
- **`ExecuteUpdateAsync` always in private method** — never inline in `Handle`. Name after intent (e.g. `IncrementFollowerCount`, `DecrementFollowerCount`). Return `Task<int>` (not `Task`) to match return type exactly, avoid implicit upcasting overhead.
- **Use transaction when `SaveChangesAsync` + `ExecuteUpdateAsync` must be atomic** — wrap in `await using var tx = await db.Database.BeginTransactionAsync(ct)`, call `await tx.CommitAsync(ct)` at end. `await using` ensures auto-rollback on failure.
- **Begin transaction before any read that participates in write sequence** — any `FirstOrDefaultAsync` or `AnyAsync` whose result decides insert/update/delete must be inside transaction. Pure 404-guard reads (no corresponding write) may stay outside. Read outside + write inside = race window w/ stale read.
- **Use `ExecuteDeleteAsync` instead of `Remove` + `SaveChangesAsync` when deleted entity drives counter update** — `ExecuteDeleteAsync` returns rows actually deleted. Guard counter update on `deletedCount > 0` to prevent double-decrements under concurrent requests. Example: `RemoveReaction`, `UnfollowChannel`. Never load collection into memory to delete; use `ExecuteDeleteAsync` w/ `Where` filter.
- **Use `ExecuteUpdateAsync` w/ guard condition for idempotent state transitions** — when transition must happen exactly once (e.g. soft-delete), do `ExecuteUpdateAsync(WHERE current_state)` and check returned row count. Apply side effects (counter updates, etc.) only if count > 0. Prevents double side-effects when concurrent requests race through same guard.
- **No value objects** unless type has real validation/equality rules (none identified yet)
- **Base classes:** `BaseEntity` (Id + CreatedAt, both `init`) and `BaseAuditableEntity : BaseEntity` (+ `UpdatedAt` w/ `private set`, mutated via `SetUpdatedAt(now)`)
- **Errors live in `Domain/Errors/`**. Three kinds:
  - `CommonErrors` — generic, parameterized: `CommonErrors.NotFound("User", id)`, `CommonErrors.Unauthorized()`, `CommonErrors.Forbidden()`
  - `EntityErrors/Errors.X.cs` — one file per entity, `static partial class Errors` w/ nested static class per entity. Called as `Errors.User.IncorrectPassword()`, `Errors.Channel.NotOwner()`
  - `FeatureErrors/Errors.X.cs` — same pattern for cross-entity feature errors (e.g. upload flow)
  - Each `Error` carries `Code`, `Message`, `ErrorType` (enum). Api layer maps `ErrorType` to HTTP status codes in `ResultExtensions`.

## Fail fast in the domain, record an anomaly at a machine inbound

Two rules that look contradictory and are not. Which one applies depends on **who calls**.

- **Domain constructors and methods throw.** `ArgumentException.ThrowIfNullOrWhiteSpace`,
  `ArgumentNullException.ThrowIfNull`, overflow guards. An entity must be impossible to build in an
  invalid state, and the throw is what makes that true.
- **A handler whose caller is a machine never lets that throw escape.** Webhook endpoints
  (`VideoProcessed`, `MinioUploadCompleted`) and any future queue consumer: on a payload the domain
  refuses, write the terminal state, log it, and **return success**. Never `throw` and never return
  5xx to make the sender retry.

**Why the second rule exists — this is BUG-1, and it cost a permanent data stall.**
`VideoArtifacts` required `previewPath` and `audioPath`, but the Processor's contract says a success
webhook may omit them ([`design-decisions.md #3`](../../../VidroProcessor/docs/agents/design-decisions.md)).
A non-critical step failing (a thumbnail glitch) produced a legitimate success payload, the
constructor threw, the handler returned 500 — and from there nothing recovers: the worker retries 3
times and gives up (`internal/webhook/webhook.go`), the job was **already acked** so it never reaches
the DLQ, and the video sits in `Processing` forever. A 500 to a person is a retry; a 500 to a machine
with a finite retry budget is a silently dropped message.

In practice:

- **Whatever the contract marks optional must be nullable in the entity and in the EF configuration.**
  A `!` (null-forgiving) on a field the sender is allowed to omit is this bug waiting to happen.
- **Absent data has a meaning — decide it explicitly.** Missing optional artifact → persist without it.
  Missing *required* artifact → `Failed`, which is terminal and visible. Both are ACKs.
- **A terminal state is not enough on its own.** Pair it with a reconciliation pass for the records
  that never got any callback (`VideoReconciliationService`).
- **This does not apply to user-facing endpoints.** There, invalid input is a `Validator` returning
  400 — the caller is a person who can read it and fix the request.

## Settings / POCO conventions

- **Non-nullable `string` props** use `= null!` (not `= default!`) to suppress CS8618, intent clear.
- **Acronyms in prop names** follow PascalCase, first letter only capitalized: `UseSsl`, `HlsPath`, `ApiKey` — never `UseSSL`, `HLSPath`, `APIKey`.
- **No default values in Settings POCOs** — defaults belong in `appsettings.json`, not code. Code defaults silently mask missing config. POCO = typed shell only; `ValidateOnStart()` catches missing values at startup.

## Code readability

The language-agnostic rules — named variables over inline expressions, extracting complex logic
into a named method, names that say *what*, three-line ternaries — live once in the monorepo root
[`../../../CLAUDE.md`](../../../CLAUDE.md), section "Legibilidade". What follows is C#-specific.

- **`Handle` read like sequence of named steps** — any non-trivial inline block (query building, object construction, projection/mapping) must extract to private method. Goal: `Handle` reads top-to-bottom as descriptive calls, no impl detail.
- **`SaveChangesAsync` always stays in `Handle`** — private methods must never call `db.SaveChangesAsync`. Only stage changes (e.g. `db.Add`, `db.Remove`). Keeps persistence boundary explicit and visible.
- **Build `Response` inline in `Handle`** — only extract mapping to private method if `Response` is very large (many fields across multiple related objects). For typical responses, keep `new Response { ... }` directly in `Handle`.
- **No `Async` suffix on method names** — return type (`Task`/`ValueTask`) already communicates that.
- **Prefer `{}` block body over `=>` expression body** for methods w/ more than one line. Reserve `=>` for true one-liners (e.g. computed props on entities, simple delegating calls).
  ```csharp
  // ✅ one-liner → =>
  public int Total => Items.Count;

  // ✅ multi-line → {}
  private Task<Video?> FetchVideo(Guid id, CancellationToken ct)
  {
      return db.Videos.Include(v => v.Channel).FirstOrDefaultAsync(v => v.Id == id, ct);
  }

  // ❌ multi-line with =>
  private Task<Video?> FetchVideo(Guid id, CancellationToken ct) =>
      db.Videos.Include(v => v.Channel).FirstOrDefaultAsync(v => v.Id == id, ct);
  ```

## Route parameter conventions

- **User identity in routes** — always use `user.Username` (`string`), never `user.Id` (`Guid`). Route segment: `{username}`.
- **Channel identity in routes** — always use `channel.Handle` (`string`), never `channel.Id` (`Guid`). Route segment: `{handle}`.
- **Channel routes always scoped under user** — `/v1/users/{username}/channels/{handle}/...`. Never flat `/v1/channels/{channelId}/...` for channel-scoped resources.
- **Handler lookup pattern** — resolve channel by `(handle, username)` pair:
  ```csharp
  var channel = await db.Channels
      .FirstOrDefaultAsync(c => c.Handle == query.Handle && c.User.Username == query.Username, ct);
  ```
- **Playlist routes** — user-scoped: `GET /v1/users/{username}/playlists`. Channel playlists add handle: `GET /v1/users/{username}/channels/{handle}/playlists`.

## Testing conventions

- **Domain entities must always have unit tests** — placed in `tests/VidroApi.UnitTests/Domain/<EntityName>Tests.cs`.
- **Features must always have integration tests** — placed in `tests/VidroApi.IntegrationTests/<Domain>/<FeatureName>Tests.cs`.
- Integration tests use `ApiFactory` (`WebApplicationFactory<Program>` + Testcontainers PostgreSQL), exercise full HTTP stack.
- Use `IClassFixture<ApiFactory>` to share container across tests in class. Generate unique usernames/emails per test (e.g. `Guid.NewGuid()`) to avoid inter-test conflicts.
- Assert on both HTTP status code and response body (`code` field for errors, `data` for success).
- **Test helper pattern for channel creation** — `CreateChannelAndGetIds()` returns `(string AccessToken, string Username, string ChannelHandle)`. Video creation helpers take `username` and `channelHandle`, not IDs.
## Checklist — adding a feature slice

1. Create `src/VidroApi.Api/Features/<Domain>/<FeatureName>.cs` — one `public static class`, members
   in this order: `Request` (and `Command`, when input mixes body + claims) → `Response` →
   `Validator` → `MapEndpoint` → `Handler`.
2. **Do not register the endpoint anywhere.** `app.MapAllEndpoints()` scans the assembly by
   reflection and calls every `public static MapEndpoint`. A wrong signature does not fail the
   build — the route simply never exists, and the symptom is a 404 in a test.
3. Route follows the identity rules: `{username}` and `{handle}`, never a `Guid`; channel-scoped
   resources nest under the user.
4. `Handle` reads as a sequence of named steps. `SaveChangesAsync` stays **in** `Handle`; a read that
   decides a write goes **inside** the transaction.
5. Errors come from `Domain/Errors/` — `CommonErrors`, `Errors.<Entity>.X()`, or a new
   `FeatureErrors` entry. Return `Result.Success(response)` / the error; never throw for control flow.
5.1. **Is the caller a machine?** (webhook, queue consumer) Then no input can reach a domain throw:
   absent or malformed data becomes a terminal state plus a log, and the response is a success — see
   "Fail fast in the domain, record an anomaly at a machine inbound" above. Getting this wrong drops
   the message for good.
6. Enums in the response are `EnumValue`, and inside an EF projection they are built inline
   (`EnumValue.From` does not translate to SQL).
6.1. **A new enum goes in `Domain/Enums/`, with explicit numeric values** — never nested in the
   slice, even when only that slice uses it. If VidroFront sees it, add it to
   [`contracts/enums.json`](../../../contracts/README.md) and to the two contract tests in the
   same commit. Why, and what nesting one costs:
   [architecture.md](architecture.md#where-enums-live-and-why-it-matters).
7. Entity or mapping changed → update the `IEntityTypeConfiguration`, add the composite index for
   any new 2+ column filter ([design-decisions #4](design-decisions.md#4-composite-indexes-are-declared-for-every-common-query-pattern)),
   and create the migration: `dotnet ef migrations add <PascalCaseDescriptionMigration> --project src/VidroApi.Infrastructure --startup-project src/VidroApi.Api --output-dir Persistence/Migrations`.
8. Tests: integration test in `tests/VidroApi.IntegrationTests/<Domain>/<FeatureName>Tests.cs`
   (always), plus a unit test in `tests/VidroApi.UnitTests/Domain/` if a domain entity changed.
   Assert both the status code and the body.
9. `dotnet test`.
10. Update [`features-index.md`](features-index.md) with the file and the route. Add a numbered entry
    to [`design-decisions.md`](design-decisions.md) if the slice does something non-obvious that a
    future reader would try to "fix".

## Checklist — adding a setting

1. Add the property to a POCO in `src/VidroApi.Infrastructure/Settings/` (new file if it is a new
   section), with the `[Required]`/`[Range]` annotations. **No default value in the POCO** — a code
   default masks a missing key instead of failing at startup.
2. Register the section in `src/VidroApi.Api/SettingsRegistration.cs`:
   `.BindConfiguration("<Section>").ValidateDataAnnotations().ValidateOnStart()`.
3. Put the default in `appsettings.json`. Secrets and local endpoints go in
   `appsettings.Development.json` instead.
4. If the value differs inside the stack, add the override to the root `docker-compose.yml` as
   `Section__Key`.
5. Add the row to [`config.md`](config.md) — with the default **and why the default is that value**.
6. Inject it as `IOptions<TSettings>`; never read `IConfiguration` from a slice.
7. `dotnet test`.
