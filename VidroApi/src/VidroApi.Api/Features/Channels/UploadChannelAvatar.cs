using System.Security.Claims;
using CSharpFunctionalExtensions;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VidroApi.Api.Extensions;
using VidroApi.Application.Abstractions;
using VidroApi.Domain.Errors;
using VidroApi.Domain.Errors.EntityErrors;
using VidroApi.Infrastructure.Persistence;
using VidroApi.Infrastructure.Settings;

namespace VidroApi.Api.Features.Channels;

public static class UploadChannelAvatar
{
    public record Command : IRequest<Result<Response, Error>>
    {
        public string Handle { get; init; } = null!;
        public Guid UserId { get; init; }
    }

    public record Response
    {
        public string UploadUrl { get; init; } = null!;
        public DateTimeOffset UploadExpiresAt { get; init; }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/v1/channels/{handle}/avatar", async (
            string handle,
            ClaimsPrincipal user,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var cmd = new Command { Handle = handle, UserId = user.GetUserId() };
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
            var channel = await db.Channels
                .FirstOrDefaultAsync(c => c.Handle == cmd.Handle && c.UserId == cmd.UserId, ct);

            if (channel is null)
                return CommonErrors.NotFound(nameof(Domain.Entities.Channel), cmd.Handle);

            var objectKey = $"avatars/channels/{channel.Id}";
            var ttlHours = minioOptions.Value.UploadUrlTtlHours;
            var ttl = TimeSpan.FromHours(ttlHours);
            var uploadExpiresAt = clock.UtcNow.AddHours(ttlHours);

            var (uploadUrl, _) = await minio.GenerateUploadUrlAsync(objectKey, ttl, ct);

            channel.SetAvatar(objectKey, clock.UtcNow);
            await db.SaveChangesAsync(ct);

            return new Response
            {
                UploadUrl = uploadUrl,
                UploadExpiresAt = uploadExpiresAt
            };
        }
    }
}
