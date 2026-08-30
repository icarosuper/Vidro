using System.Security.Claims;
using CSharpFunctionalExtensions;
using Mediator;
using Microsoft.EntityFrameworkCore;
using VidroApi.Api.Extensions;
using VidroApi.Application.Abstractions;
using VidroApi.Domain.Entities;
using VidroApi.Domain.Errors;
using VidroApi.Domain.Errors.EntityErrors;
using VidroApi.Infrastructure.Persistence;

namespace VidroApi.Api.Features.Channels;

public static class DeleteChannel
{
    public record Command : IRequest<UnitResult<Error>>
    {
        public string Handle { get; init; } = null!;
        public Guid UserId { get; init; }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/v1/channels/{handle}", async (
            string handle,
            ClaimsPrincipal user,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var cmd = new Command
            {
                Handle = handle,
                UserId = user.GetUserId()
            };
            var result = await mediator.Send(cmd, ct);
            return result.ToApiResult();
        })
        .RequireAuthorization();

    public class Handler(AppDbContext db, IDateTimeProvider clock) : IRequestHandler<Command, UnitResult<Error>>
    {
        public async ValueTask<UnitResult<Error>> Handle(Command cmd, CancellationToken ct)
        {
            var channel = await db.Channels
                .FirstOrDefaultAsync(c => c.Handle == cmd.Handle && c.UserId == cmd.UserId, ct);

            if (channel is null)
                return CommonErrors.NotFound(nameof(Channel), cmd.Handle);

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            await StageStorageCleanup(channel.Id, ct);

            db.Channels.Remove(channel);
            await db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);

            return UnitResult.Success<Error>();
        }

        private async Task StageStorageCleanup(Guid channelId, CancellationToken ct)
        {
            var now = clock.UtcNow;

            var videoIds = await db.Videos
                .Where(v => v.ChannelId == channelId)
                .Select(v => v.Id)
                .ToListAsync(ct);

            foreach (var videoId in videoIds)
                db.PendingStorageCleanups.Add(new PendingStorageCleanup($"raw/{videoId}", isPrefix: false, now));

            var artifacts = await db.VideoArtifacts
                .Where(a => videoIds.Contains(a.VideoId))
                .Select(a => new { a.VideoId, a.ProcessedPath, a.PreviewPath, a.AudioPath, a.HlsPath })
                .ToListAsync(ct);

            foreach (var artifact in artifacts)
            {
                db.PendingStorageCleanups.Add(new PendingStorageCleanup(artifact.ProcessedPath, isPrefix: false, now));
                if (artifact.PreviewPath is not null)
                    db.PendingStorageCleanups.Add(new PendingStorageCleanup(artifact.PreviewPath, isPrefix: false, now));
                if (artifact.AudioPath is not null)
                    db.PendingStorageCleanups.Add(new PendingStorageCleanup(artifact.AudioPath, isPrefix: false, now));
                if (artifact.HlsPath is not null)
                    db.PendingStorageCleanups.Add(new PendingStorageCleanup(artifact.HlsPath, isPrefix: true, now));
                db.PendingStorageCleanups.Add(new PendingStorageCleanup($"thumbnails/{artifact.VideoId}/", isPrefix: true, now));
            }
        }
    }
}
