using System.Security.Claims;
using CSharpFunctionalExtensions;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VidroApi.Api.Extensions;
using VidroApi.Application.Abstractions;
using VidroApi.Domain.Enums;
using VidroApi.Domain.Errors;
using VidroApi.Domain.Errors.EntityErrors;
using VidroApi.Infrastructure.Persistence;
using VidroApi.Infrastructure.Settings;

namespace VidroApi.Api.Features.Videos;

public static class UploadVideoThumbnail
{
    public record Command : IRequest<Result<Response, Error>>
    {
        public Guid VideoId { get; init; }
        public Guid UserId { get; init; }
    }

    public record Response
    {
        public string UploadUrl { get; init; } = null!;
        public DateTimeOffset UploadExpiresAt { get; init; }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/v1/videos/{videoId:guid}/thumbnail", async (
            Guid videoId,
            ClaimsPrincipal user,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var cmd = new Command { VideoId = videoId, UserId = user.GetUserId() };
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
            var video = await FetchVideo(cmd.VideoId, ct);

            if (video is null)
                return CommonErrors.NotFound(nameof(Domain.Entities.Video), cmd.VideoId);

            var isOwner = video.Channel.UserId == cmd.UserId;
            if (!isOwner)
            {
                return video.IsPrivate
                    ? CommonErrors.NotFound(nameof(Domain.Entities.Video), cmd.VideoId)
                    : Errors.Video.NotOwner();
            }

            var objectKey = $"thumbnails/{cmd.VideoId}/custom.jpg";
            var ttlHours = minioOptions.Value.UploadUrlTtlHours;
            var ttl = TimeSpan.FromHours(ttlHours);
            var uploadExpiresAt = clock.UtcNow.AddHours(ttlHours);

            var (uploadUrl, _) = await minio.GenerateUploadUrlAsync(objectKey, ttl, ct);

            if (video.Artifacts is not null)
                await SetCustomThumbnailPath(cmd.VideoId, objectKey, ct);

            return new Response
            {
                UploadUrl = uploadUrl,
                UploadExpiresAt = uploadExpiresAt
            };
        }

        private Task<Domain.Entities.Video?> FetchVideo(Guid videoId, CancellationToken ct)
        {
            return db.Videos
                .Include(v => v.Channel)
                .Include(v => v.Artifacts)
                .FirstOrDefaultAsync(v => v.Id == videoId, ct);
        }

        private Task<int> SetCustomThumbnailPath(Guid videoId, string path, CancellationToken ct)
        {
            return db.VideoArtifacts
                .Where(a => a.VideoId == videoId)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.CustomThumbnailPath, path), ct);
        }
    }
}
