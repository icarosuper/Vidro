using System.Security.Claims;
using CSharpFunctionalExtensions;
using FluentValidation;
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

public static class ListChannelVideos
{
    public record Command : IRequest<Result<Response, Error>>
    {
        public string Username { get; init; } = null!;
        public string ChannelHandle { get; init; } = null!;
        public Guid? RequestingUserId { get; init; }
        public DateTimeOffset? Cursor { get; init; }
        public int Limit { get; init; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator(IOptions<ListChannelVideosSettings> options)
        {
            RuleFor(x => x.Limit)
                .InclusiveBetween(1, options.Value.MaxLimit)
                .WithMessage(x => $"Limit must be between 1 and {options.Value.MaxLimit}.");
        }
    }

    public record Response
    {
        public List<VideoSummary> Videos { get; init; } = [];
        public DateTimeOffset? NextCursor { get; init; }
        
        public record VideoSummary
        {
            public Guid VideoId { get; init; }
            public string ChannelHandle { get; init; } = null!;
            public string OwnerUsername { get; init; } = null!;
            public string Title { get; init; } = null!;
            public string? Description { get; init; }
            public List<string> Tags { get; init; } = [];
            public EnumValue Visibility { get; init; } = null!;
            public EnumValue Status { get; init; } = null!;
            public int ViewCount { get; init; }
            public int LikeCount { get; init; }
            public List<string> ThumbnailUrls { get; init; } = [];
            public string? ChannelAvatarUrl { get; init; }
            public DateTimeOffset CreatedAt { get; init; }
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/v1/users/{username}/channels/{handle}/videos", async (
            string username,
            string handle,
            ClaimsPrincipal user,
            IMediator mediator,
            DateTimeOffset? cursor,
            int limit,
            CancellationToken ct = default) =>
        {
            Guid? requestingUserId = user.Identity?.IsAuthenticated == true
                ? user.GetUserId()
                : null;
            var cmd = new Command
            {
                Username = username,
                ChannelHandle = handle,
                RequestingUserId = requestingUserId,
                Cursor = cursor,
                Limit = limit
            };
            var result = await mediator.Send(cmd, ct);
            return result.ToApiResult(StatusCodes.Status200OK);
        });

    public class Handler(AppDbContext db, IMinioService minio, IOptions<MinioSettings> minioOptions)
        : IRequestHandler<Command, Result<Response, Error>>
    {
        private readonly TimeSpan _thumbnailUrlTtl = TimeSpan.FromHours(minioOptions.Value.ThumbnailUrlTtlHours);

        public async ValueTask<Result<Response, Error>> Handle(Command cmd, CancellationToken ct)
        {
            var channel = await db.Channels
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.Handle == cmd.ChannelHandle && c.User.Username == cmd.Username, ct);
            if (channel is null)
                return CommonErrors.NotFound(nameof(Domain.Entities.Channel), $"{cmd.Username}/{cmd.ChannelHandle}");

            var isOwner = channel.UserId == cmd.RequestingUserId;
            var videos = await FetchChannelVideos(channel.Id, isOwner, cmd.Cursor, cmd.Limit, ct);

            var thumbnailUrlLists = await GetThumbnails(videos);
            var channelAvatarUrl = await GenerateAvatarUrl(channel.AvatarPath);
            var summaries = videos.Select((v, i) => MapToSummary(v, thumbnailUrlLists[i], channel.Handle, channel.User.Username, channelAvatarUrl)).ToList();

            var nextCursor = videos.Count == cmd.Limit
                ? videos[^1].CreatedAt
                : (DateTimeOffset?)null;

            return new Response
            {
                Videos = summaries,
                NextCursor = nextCursor
            };
        }

        private static Response.VideoSummary MapToSummary(Domain.Entities.Video video, List<string> thumbnailUrls, string channelHandle, string ownerUsername, string? channelAvatarUrl)
        {
            return new Response.VideoSummary
            {
                VideoId = video.Id,
                ChannelHandle = channelHandle,
                OwnerUsername = ownerUsername,
                Title = video.Title,
                Description = video.Description,
                Tags = video.Tags,
                Visibility = new EnumValue { Id = (int)video.Visibility, Value = video.Visibility.ToString() },
                Status = new EnumValue { Id = (int)video.Status, Value = video.Status.ToString() },
                ViewCount = video.ViewCount,
                LikeCount = video.LikeCount,
                ThumbnailUrls = thumbnailUrls,
                ChannelAvatarUrl = channelAvatarUrl,
                CreatedAt = video.CreatedAt
            };
        }

        private Task<List<Domain.Entities.Video>> FetchChannelVideos(
            Guid channelId, bool isOwner, DateTimeOffset? cursor, int limit, CancellationToken ct)
        {
            var query = db.Videos
                .Include(v => v.Artifacts)
                .Where(v => v.ChannelId == channelId)
                .AsQueryable();

            if (!isOwner)
                query = query.Where(v => v.Visibility == VideoVisibility.Public
                                         && v.Status == VideoStatus.Ready);

            if (cursor.HasValue)
                query = query.Where(v => v.CreatedAt < cursor.Value);

            return query
                .OrderByDescending(v => v.CreatedAt)
                .Take(limit)
                .ToListAsync(ct);
        }

        private async Task<List<List<string>>> GetThumbnails(List<Domain.Entities.Video> videos)
        {
            var thumbs = await Task.WhenAll(videos.Select(GenerateThumbnailUrls));
            return thumbs.ToList();
        }

        private async Task<string?> GenerateAvatarUrl(string? path)
        {
            if (path is null)
                return null;

            return await minio.GenerateDownloadUrlAsync(path, _thumbnailUrlTtl);
        }

        private async Task<List<string>> GenerateThumbnailUrls(Domain.Entities.Video video)
        {
            if (video.Artifacts is null)
                return [];

            var paths = new List<string>();
            if (video.Artifacts.CustomThumbnailPath is not null)
                paths.Add(video.Artifacts.CustomThumbnailPath);
            paths.AddRange(video.Artifacts.ThumbnailPaths);

            var urls = await Task.WhenAll(paths.Select(p => minio.GenerateDownloadUrlAsync(p, _thumbnailUrlTtl)));
            return [..urls];
        }
    }
}
