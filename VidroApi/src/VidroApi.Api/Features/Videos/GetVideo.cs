using System.Security.Claims;
using CSharpFunctionalExtensions;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VidroApi.Api.Common;
using VidroApi.Api.Extensions;
using VidroApi.Application.Abstractions;
using VidroApi.Domain.Enums;
using VidroApi.Domain.Errors;
using VidroApi.Infrastructure.Persistence;
using VidroApi.Infrastructure.Settings;

namespace VidroApi.Api.Features.Videos;

public static class GetVideo
{
    public record Command : IRequest<Result<Response, Error>>
    {
        public Guid VideoId { get; init; }
        public Guid? RequestingUserId { get; init; }
    }

    public record Response
    {
        public Guid VideoId { get; init; }
        public Guid ChannelId { get; init; }
        public string ChannelHandle { get; init; } = null!;
        public string ChannelName { get; init; } = null!;
        public string? ChannelAvatarUrl { get; init; }
        public string OwnerUsername { get; init; } = null!;
        public string Title { get; init; } = null!;
        public string? Description { get; init; }
        public List<string> Tags { get; init; } = [];
        public EnumValue Visibility { get; init; } = null!;
        public EnumValue Status { get; init; } = null!;
        public int ViewCount { get; init; }
        public int LikeCount { get; init; }
        public int DislikeCount { get; init; }
        public int CommentCount { get; init; }
        public List<string> ThumbnailUrls { get; init; } = [];
        public string? VideoUrl { get; init; }
        public EnumValue? UserReaction { get; init; }
        public bool IsFollowingChannel { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/v1/videos/{videoId:guid}", async (
            Guid videoId,
            ClaimsPrincipal user,
            IMediator mediator,
            CancellationToken ct) =>
        {
            Guid? requestingUserId = user.Identity?.IsAuthenticated == true
                ? user.GetUserId()
                : null;
            var cmd = new Command { VideoId = videoId, RequestingUserId = requestingUserId };
            var result = await mediator.Send(cmd, ct);
            return result.ToApiResult(StatusCodes.Status200OK);
        });

    public class Handler(AppDbContext db, IMinioService minio, IOptions<MinioSettings> minioOptions)
        : IRequestHandler<Command, Result<Response, Error>>
    {
        private readonly TimeSpan _thumbnailUrlTtl = TimeSpan.FromHours(minioOptions.Value.ThumbnailUrlTtlHours);
        private readonly TimeSpan _videoUrlTtl = TimeSpan.FromHours(minioOptions.Value.VideoUrlTtlHours);

        public async ValueTask<Result<Response, Error>> Handle(Command cmd, CancellationToken ct)
        {
            var video = await FetchVideo(cmd.VideoId, cmd.RequestingUserId, ct);

            if (video is null)
                return CommonErrors.NotFound(nameof(Domain.Entities.Video), cmd.VideoId);

            var thumbnailUrls = await GenerateThumbnailUrls(video.Artifacts);
            var videoUrl = await GenerateUrlAsync(video.Artifacts?.ProcessedPath, _videoUrlTtl);
            var channelAvatarUrl = await GenerateUrlAsync(video.Channel.AvatarPath, _thumbnailUrlTtl);
            var isFollowingChannel = await CheckIfFollowingChannel(video.ChannelId, cmd.RequestingUserId, ct);
            var userReaction = await FetchUserReaction(video.Id, cmd.RequestingUserId, ct);

            return new Response
            {
                VideoId = video.Id,
                ChannelId = video.ChannelId,
                ChannelHandle = video.Channel.Handle,
                ChannelName = video.Channel.Name,
                ChannelAvatarUrl = channelAvatarUrl,
                OwnerUsername = video.Channel.User.Username,
                Title = video.Title,
                Description = video.Description,
                Tags = video.Tags,
                Visibility = EnumValue.From(video.Visibility),
                Status = EnumValue.From(video.Status),
                ViewCount = video.ViewCount,
                LikeCount = video.LikeCount,
                DislikeCount = video.DislikeCount,
                CommentCount = video.CommentCount,
                ThumbnailUrls = thumbnailUrls,
                VideoUrl = videoUrl,
                UserReaction = userReaction,
                IsFollowingChannel = isFollowingChannel,
                CreatedAt = video.CreatedAt
            };
        }

        private Task<Domain.Entities.Video?> FetchVideo(Guid videoId, Guid? requestingUserId, CancellationToken ct)
        {
            return db.Videos
                .Include(v => v.Channel).ThenInclude(c => c.User)
                .Include(v => v.Artifacts)
                .FirstOrDefaultAsync(v => v.Id == videoId
                    && (v.Channel.UserId == requestingUserId
                        || (v.Status == VideoStatus.Ready && v.Visibility != VideoVisibility.Private)),
                    ct);
        }

        private Task<EnumValue?> FetchUserReaction(Guid videoId, Guid? requestingUserId, CancellationToken ct)
        {
            if (requestingUserId is null)
                return Task.FromResult<EnumValue?>(null);

            return db.Reactions
                .Where(r => r.VideoId == videoId && r.UserId == requestingUserId)
                .Select(r => new EnumValue { Id = (int)r.Type, Value = r.Type.ToString() })
                .FirstOrDefaultAsync(ct);
        }

        private Task<bool> CheckIfFollowingChannel(Guid channelId, Guid? requestingUserId, CancellationToken ct)
        {
            if (requestingUserId is null)
                return Task.FromResult(false);

            return db.ChannelFollowers.AnyAsync(f => f.ChannelId == channelId && f.UserId == requestingUserId, ct);
        }

        private async Task<List<string>> GenerateThumbnailUrls(Domain.Entities.VideoArtifacts? artifacts)
        {
            if (artifacts is null)
                return [];

            var paths = new List<string>();
            if (artifacts.CustomThumbnailPath is not null)
                paths.Add(artifacts.CustomThumbnailPath);
            paths.AddRange(artifacts.ThumbnailPaths);

            var urls = await Task.WhenAll(paths.Select(p => minio.GenerateDownloadUrlAsync(p, _thumbnailUrlTtl)));
            return [..urls];
        }

        private async Task<string?> GenerateUrlAsync(string? path, TimeSpan ttl)
        {
            if (path is null)
                return null;

            return await minio.GenerateDownloadUrlAsync(path, ttl);
        }
    }
}
