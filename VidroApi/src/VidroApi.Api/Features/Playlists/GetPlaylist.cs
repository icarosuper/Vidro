using System.Security.Claims;
using CSharpFunctionalExtensions;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VidroApi.Api.Common;
using VidroApi.Api.Extensions;
using VidroApi.Application.Abstractions;
using VidroApi.Domain.Errors;
using VidroApi.Infrastructure.Persistence;
using VidroApi.Infrastructure.Settings;

namespace VidroApi.Api.Features.Playlists;

public static class GetPlaylist
{
    public record Command : IRequest<Result<Response, Error>>
    {
        public Guid PlaylistId { get; init; }
        public Guid? RequestingUserId { get; init; }
    }

    public record Response
    {
        public Guid PlaylistId { get; init; }
        public string Name { get; init; } = null!;
        public string? Description { get; init; }
        public EnumValue Visibility { get; init; } = null!;
        public EnumValue Scope { get; init; } = null!;
        public int VideoCount { get; init; }
        public Guid? ChannelId { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public List<PlaylistItemResponse> Items { get; init; } = [];

        public record PlaylistItemResponse
        {
            public Guid VideoId { get; init; }
            public string Title { get; init; } = null!;
            public string? ThumbnailUrl { get; init; }
            public double? DurationSeconds { get; init; }
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/v1/playlists/{playlistId:guid}", async (
            Guid playlistId,
            ClaimsPrincipal user,
            IMediator mediator,
            CancellationToken ct) =>
        {
            Guid? requestingUserId = user.Identity?.IsAuthenticated == true
                ? user.GetUserId()
                : null;
            var cmd = new Command { PlaylistId = playlistId, RequestingUserId = requestingUserId };
            var result = await mediator.Send(cmd, ct);
            return result.ToApiResult(StatusCodes.Status200OK);
        });

    public class Handler(AppDbContext db, IMinioService minio, IOptions<MinioSettings> minioOptions)
        : IRequestHandler<Command, Result<Response, Error>>
    {
        private readonly TimeSpan _thumbnailUrlTtl = TimeSpan.FromHours(minioOptions.Value.ThumbnailUrlTtlHours);

        public async ValueTask<Result<Response, Error>> Handle(Command cmd, CancellationToken ct)
        {
            var playlist = await FetchPlaylist(cmd.PlaylistId, ct);

            if (playlist is null)
                return CommonErrors.NotFound(nameof(Domain.Entities.Playlist), cmd.PlaylistId);

            var isOwner = playlist.UserId == cmd.RequestingUserId;
            var isPrivate = playlist.Visibility == Domain.Enums.PlaylistVisibility.Private;
            if (isPrivate && !isOwner)
                return CommonErrors.NotFound(nameof(Domain.Entities.Playlist), cmd.PlaylistId);

            var items = await BuildItemResponses(playlist);

            return new Response
            {
                PlaylistId = playlist.Id,
                Name = playlist.Name,
                Description = playlist.Description,
                Visibility = EnumValue.From(playlist.Visibility),
                Scope = EnumValue.From(playlist.Scope),
                VideoCount = playlist.VideoCount,
                ChannelId = playlist.ChannelId,
                CreatedAt = playlist.CreatedAt,
                Items = items
            };
        }

        private Task<Domain.Entities.Playlist?> FetchPlaylist(Guid playlistId, CancellationToken ct)
        {
#pragma warning disable CS8602
            return db.Playlists
                .Include(p => p.Items)
                    .ThenInclude(pi => pi.Video)
                        .ThenInclude(v => v.Artifacts)
                .Include(p => p.Items)
                    .ThenInclude(pi => pi.Video)
                        .ThenInclude(v => v.Metadata)
                .FirstOrDefaultAsync(p => p.Id == playlistId, ct);
#pragma warning restore CS8602
        }

        private async Task<List<Response.PlaylistItemResponse>> BuildItemResponses(
            Domain.Entities.Playlist playlist)
        {
            var validItems = playlist.Items
                .Where(pi => pi.VideoId.HasValue && pi.Video is not null)
                .OrderBy(pi => pi.CreatedAt)
                .ToList();

            var thumbnailUrls = await Task.WhenAll(validItems.Select(GenerateThumbnailUrl));

            return validItems.Select((item, i) => new Response.PlaylistItemResponse
            {
                VideoId = item.Video!.Id,
                Title = item.Video.Title,
                ThumbnailUrl = thumbnailUrls[i],
                DurationSeconds = item.Video.Metadata?.DurationSeconds
            }).ToList();
        }

        private async Task<string?> GenerateThumbnailUrl(Domain.Entities.PlaylistItem item)
        {
            var video = item.Video;
            if (video is null)
                return null;

            var artifacts = video.Artifacts;
            if (artifacts is null)
                return null;

            var thumbnailPath = artifacts.CustomThumbnailPath ?? artifacts.ThumbnailPaths.FirstOrDefault();
            if (thumbnailPath is null)
                return null;

            return await minio.GenerateDownloadUrlAsync(thumbnailPath, _thumbnailUrlTtl);
        }
    }
}
