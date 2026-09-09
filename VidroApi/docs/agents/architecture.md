# Architecture

## Overview

**Clean Architecture + Vertical Slice Architecture.** Each feature: single self-contained file under `src/VidroApi.Api/Features/<Domain>/FeatureName.cs`.

Features in Api project (not Application) → free access to `AppDbContext`, BCrypt, Infrastructure types, no circular deps. Application: shared abstractions, behaviors, `PagedResult` only.

### Project dependency flow

```
Domain ← Application ← Infrastructure ← Api
```

- **Domain** — entities, enums, `DomainError`. No external deps.
- **Application** — defines interfaces (`IMinioService`, `IJobQueueService`). No EF Core.
- **Infrastructure** — EF Core `AppDbContext`, `MinioService`, `RedisJobQueueService`, `TokenService`, `DateTimeProvider`, settings. All external I/O here. Entity mappings: `IEntityTypeConfiguration<T>` (one file/entity in `Persistence/Configurations/`). `OnModelCreating` applies `DeleteBehavior.Cascade` globally for all FKs.
- **Api** — `Program.cs` only. Registers DI, middleware, JWT and the background services (`BackgroundServices/`), then calls `app.MapAllEndpoints()` — which discovers every slice by reflection. Slices are **never** registered by hand.

## Vertical Slice pattern

Every slice in `Api/Features/<Domain>/` — exact order:

```csharp
public static class FeatureName
{
    public record Request : IRequest<Result<Response, Error>>
    {
        public string Foo { get; init; } = null!;
    }

    public record Response
    {
        public Guid Id { get; init; }
    }

    public class Validator : AbstractValidator<Request> { ... }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/v1/...", async (Request req, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(req, ct);
            return result.ToApiResult(StatusCodes.Status201Created);
        });

    public class Handler(AppDbContext db, IDateTimeProvider clock, ...)
        : IRequestHandler<Request, Result<Response, Error>>
    {
        public async Task<Result<Response, Error>> Handle(Request req, CancellationToken ct) { ... }
    }
}
```

- **Order:** Request → Response → Validator → MapEndpoint → Handler.
- **`Request` vs `Command`** — use `Request : IRequest<...>` when body = only input. Multi-source (body + JWT claims): `Request` for HTTP body, separate `Command : IRequest<...>` for Mediator; endpoint constructs `Command` from both. Ex: `SignOut` has `Request { RefreshToken }` (body) + `Command { UserId, RefreshToken }` (body + claim).
- **Request/Response format:** `record` with `init` props (one/line), not positional records.
- **No magic numbers in Validators** — use entity constants (e.g. `User.UsernameMinLength`, `User.PasswordMinLength`). Same constants in entity constructor guards.
- **Endpoint registration automatic** — `app.MapAllEndpoints()` in `Program.cs` scans assembly by reflection, calls every public static `MapEndpoint`. Never register manually.
- **`result.ToApiResult(statusCode)`** maps `Result<T, Error>` → correct HTTP response: success wraps in `{ "data": ... }`, failure maps `ErrorType` → status code, returns `{ "code": ..., "message": ... }`.
- Handlers return `Result<Response, Error>` (CSharpFunctionalExtensions). Use `Result.Success(response)` or `DomainError.X.Y()`.
- Validation auto via `ValidationBehavior<,>` (MediatR pipeline). FluentValidation exceptions → middleware → 400.
- Inject `IDateTimeProvider` into handlers needing current time → pass to entity constructors.

## Response format

```json
// Success (200/201)
{ "data": { ... } }

// Error (400/401/403/404/409)
{ "code": "user.not_found", "message": "User with id '...' was not found." }
```

`ApiResponse` and `ResultExtensions` in `Api/Common/` and `Api/Extensions/`.

## Enums in responses

**Never return raw enum string or raw integer.** Always use `EnumValue` (`Api/Common/EnumValue.cs`) → consumer gets both numeric ID + human-readable string:

```json
{ "visibility": { "id": 0, "value": "Public" } }
```

Non-LINQ context, use static helper:
```csharp
Visibility = EnumValue.From(video.Visibility),
```

EF Core LINQ projections (`.Select(v => ...)`), use inline construction (`EnumValue.From` = custom method, untranslatable to SQL):
```csharp
Visibility = new EnumValue { Id = (int)v.Visibility, Value = v.Visibility.ToString() },
```

### Where enums live, and why it matters

**Every enum that crosses the API boundary goes in `Domain/Enums/`, with explicit numeric values**
— never nested inside a feature slice, even when only one slice uses it. `CommentSortOrder` was
nested in `ListComments` until 2026-09-09; the reason to move it is not tidiness:

- VidroFront mirrors these enums **by hand** (it receives them as integers), and the golden that
  pins them is [`contracts/enums.json`](../../../contracts/README.md). One flat place to look is
  what makes "are these six still right?" answerable.
- The contract test that reads that golden
  (`tests/VidroApi.UnitTests/Contracts/EnumContractTests.cs`) only needs `VidroApi.Domain`. An
  enum nested in a slice would force the test into a project that references `VidroApi.Api` —
  which is how it started, and it dragged Docker-bound infrastructure along for a pure
  reflection test.

Explicit values (`Recent = 0`) because the wire format **is** the number: an implicit value is a
contract decided by declaration order.

## Auth

DIY JWT — no ASP.NET Core Identity. `TokenService` (Infrastructure): access tokens (15 min), refresh tokens (7 days). Refresh tokens stored in `RefreshTokens` table, rotated on use. Extract `UserId` via `ctx.User.GetUserId()` (`ClaimsPrincipal` extension).

## Integration with VideoProcessor (Go)

VideoProcessor = separate service at `../VidroProcessor`. Integration points:

1. **Upload** — API writes raw video to MinIO at `raw/{videoId}` via presigned PUT URL (client uploads directly, never through API).
2. **Enqueue** — `IJobQueueService.PublishJobAsync(videoId, callbackUrl)` writes `job:{videoId}` key to Redis, pushes `videoId` to `video_queue`.
3. **Webhook** — VideoProcessor calls `POST /webhooks/video-processed` when done. Validated with HMAC-SHA256 (`X-Webhook-Signature: sha256=...`). Secret shared via `Webhook:Secret` config.

### MinIO object paths (shared contract with VideoProcessor)

| Path | Owner | Content |
|---|---|---|
| `raw/{videoId}` | API | Original upload |
| `processed/{videoId}_processed` | VideoProcessor | Transcoded video |
| `thumbnails/{videoId}/` | VideoProcessor | 5 JPG frames |
| `audio/{videoId}.mp3` | VideoProcessor | Audio track |
| `preview/{videoId}_preview.mp4` | VideoProcessor | Low-quality preview |
| `hls/{videoId}/` | VideoProcessor | HLS segments + playlist |

## Configuration

Every key, its default and why the default is that value: [config.md](config.md). The two that bind
this service to the worker are `JobQueueSettings:QueueName` (must equal the worker's
`PROCESSING_REQUEST_QUEUE`) and `VideoSettings:ProcessingTimeoutMinutes` (must stay above the
worker's job budget + orphan-requeue threshold).
