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

namespace VidroApi.Api.Features.Videos;

public static class DeleteVideo
{
    public record Command : IRequest<UnitResult<Error>>
    {
        public Guid VideoId { get; init; }
        public Guid UserId { get; init; }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/v1/videos/{videoId:guid}", async (
            Guid videoId,
            ClaimsPrincipal user,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var cmd = new Command { VideoId = videoId, UserId = user.GetUserId() };
            var result = await mediator.Send(cmd, ct);
            return result.ToApiResult();
        })
        .RequireAuthorization();

    public class Handler(AppDbContext db, IDateTimeProvider clock)
        : IRequestHandler<Command, UnitResult<Error>>
    {
        public async ValueTask<UnitResult<Error>> Handle(Command cmd, CancellationToken ct)
        {
            var video = await FetchVideo(cmd.VideoId, ct);

            if (video is null)
                return CommonErrors.NotFound(nameof(Video), cmd.VideoId);

            var isOwner = video.Channel.UserId == cmd.UserId;
            if (!isOwner)
                return video.IsPrivate
                    ? CommonErrors.NotFound(nameof(Video), cmd.VideoId)
                    : Errors.Video.NotOwner();

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            StageStorageCleanup(video, clock.UtcNow);

            var deletedVideoRows = await db.Videos.Where(v => v.Id == cmd.VideoId).ExecuteDeleteAsync(ct);
            if (deletedVideoRows == 0)
                return CommonErrors.NotFound(nameof(Video), cmd.VideoId);

            await db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);

            return UnitResult.Success<Error>();
        }

        private Task<Video?> FetchVideo(Guid videoId, CancellationToken ct)
        {
            return db.Videos
                .Include(v => v.Channel)
                .Include(v => v.Artifacts)
                .FirstOrDefaultAsync(v => v.Id == videoId, ct);
        }

        private void StageStorageCleanup(Video video, DateTimeOffset now)
        {
            db.PendingStorageCleanups.Add(new PendingStorageCleanup($"raw/{video.Id}", isPrefix: false, now));

            if (video.Artifacts is null)
                return;

            db.PendingStorageCleanups.Add(new PendingStorageCleanup(video.Artifacts.ProcessedPath, isPrefix: false, now));
            if (video.Artifacts.PreviewPath is not null)
                db.PendingStorageCleanups.Add(new PendingStorageCleanup(video.Artifacts.PreviewPath, isPrefix: false, now));
            if (video.Artifacts.AudioPath is not null)
                db.PendingStorageCleanups.Add(new PendingStorageCleanup(video.Artifacts.AudioPath, isPrefix: false, now));
            if (video.Artifacts.HlsPath is not null)
                db.PendingStorageCleanups.Add(new PendingStorageCleanup(video.Artifacts.HlsPath, isPrefix: true, now));
            db.PendingStorageCleanups.Add(new PendingStorageCleanup($"thumbnails/{video.Id}/", isPrefix: true, now));
        }
    }
}
