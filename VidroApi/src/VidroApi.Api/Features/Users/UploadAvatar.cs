using System.Security.Claims;
using CSharpFunctionalExtensions;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VidroApi.Api.Extensions;
using VidroApi.Application.Abstractions;
using VidroApi.Domain.Errors;
using VidroApi.Infrastructure.Persistence;
using VidroApi.Infrastructure.Settings;

namespace VidroApi.Api.Features.Users;

public static class UploadAvatar
{
    public record Command : IRequest<Result<Response, Error>>
    {
        public Guid UserId { get; init; }
    }

    public record Response
    {
        public string UploadUrl { get; init; } = null!;
        public DateTimeOffset UploadExpiresAt { get; init; }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/v1/users/me/avatar", async (
            ClaimsPrincipal user,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var cmd = new Command { UserId = user.GetUserId() };
            var result = await mediator.Send(cmd, ct);
            return result.ToApiResult(StatusCodes.Status200OK);
        })
        .RequireAuthorization();

    public class Handler(
        AppDbContext db,
        IMinioService minio,
        IDateTimeProvider clock,
        IOptions<MinioSettings> minioOptions)
        : IRequestHandler<Command, Result<Response, Error>>
    {
        public async ValueTask<Result<Response, Error>> Handle(Command cmd, CancellationToken ct)
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == cmd.UserId, ct);

            if (user is null)
                return CommonErrors.NotFound(nameof(Domain.Entities.User), cmd.UserId);

            var objectKey = $"avatars/{cmd.UserId}";
            var ttlHours = minioOptions.Value.UploadUrlTtlHours;
            var ttl = TimeSpan.FromHours(ttlHours);
            var uploadExpiresAt = clock.UtcNow.AddHours(ttlHours);

            var (uploadUrl, _) = await minio.GenerateUploadUrlAsync(objectKey, ttl, ct);

            user.SetAvatar(objectKey);
            await db.SaveChangesAsync(ct);

            return new Response
            {
                UploadUrl = uploadUrl,
                UploadExpiresAt = uploadExpiresAt
            };
        }
    }
}
