using System.Security.Claims;
using CSharpFunctionalExtensions;
using Mediator;
using Microsoft.EntityFrameworkCore;
using VidroApi.Api.Common;
using VidroApi.Api.Extensions;
using VidroApi.Application.Common;
using VidroApi.Domain.Errors;
using VidroApi.Domain.Enums;
using VidroApi.Infrastructure.Persistence;

namespace VidroApi.Api.Features.Playlists;

public static class ListPlaylistsByUser
{
    public record Query : IRequest<Result<PagedResult<PlaylistResponse>, Error>>
    {
        public string Username { get; init; } = null!;
        public Guid? RequestingUserId { get; init; }
        public string? Cursor { get; init; }
        public int Limit { get; init; } = 20;
        // Optional: asks whether each playlist already holds this video (the "Save" picker).
        public Guid? VideoId { get; init; }
    }

    public record PlaylistResponse
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = null!;
        public string? Description { get; init; }
        public EnumValue Visibility { get; init; } = null!;
        public int VideoCount { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        // Null unless the request passed `videoId`.
        public bool? ContainsVideo { get; init; }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/v1/users/{username}/playlists", async (
            string username,
            string? cursor,
            int limit,
            Guid? videoId,
            ClaimsPrincipal user,
            IMediator mediator,
            CancellationToken ct) =>
        {
            Guid? requestingUserId = user.Identity?.IsAuthenticated == true
                ? user.GetUserId()
                : null;

            var query = new Query
            {
                Username = username,
                RequestingUserId = requestingUserId,
                Cursor = cursor,
                Limit = Math.Max(1, Math.Min(limit, 100)),
                VideoId = videoId
            };

            var result = await mediator.Send(query, ct);
            return result.ToApiResult(StatusCodes.Status200OK);
        })
        .ProducesApiSuccess<PagedResult<PlaylistResponse>>();

    public class Handler(AppDbContext db)
        : IRequestHandler<Query, Result<PagedResult<PlaylistResponse>, Error>>
    {
        public async ValueTask<Result<PagedResult<PlaylistResponse>, Error>> Handle(
            Query query, CancellationToken ct)
        {
            var playlists = await FetchPlaylists(query, ct);
            var playlistsWithVideo = await FetchPlaylistsContainingVideo(playlists, query.VideoId, ct);

            var responses = playlists
                .Select(p => new PlaylistResponse
                {
                    Id = p.Id,
                    Name = p.Name,
                    Description = p.Description,
                    Visibility = EnumValue.From(p.Visibility),
                    VideoCount = p.VideoCount,
                    CreatedAt = p.CreatedAt,
                    ContainsVideo = playlistsWithVideo?.Contains(p.Id)
                })
                .ToList();

            string? nextCursor = null;
            if (playlists.Count == query.Limit + 1)
            {
                nextCursor = playlists[query.Limit].CreatedAt.ToString("O");
                responses.RemoveAt(responses.Count - 1);
            }

            return new PagedResult<PlaylistResponse>(responses, nextCursor);
        }

        // One query for the whole page instead of one per playlist; null when nobody asked.
        private async Task<HashSet<Guid>?> FetchPlaylistsContainingVideo(
            List<Domain.Entities.Playlist> playlists, Guid? videoId, CancellationToken ct)
        {
            if (videoId is null)
                return null;

            var playlistIds = playlists.Select(p => p.Id).ToList();
            var containing = await db.PlaylistItems
                .Where(pi => pi.VideoId == videoId && playlistIds.Contains(pi.PlaylistId))
                .Select(pi => pi.PlaylistId)
                .ToListAsync(ct);
            return containing.ToHashSet();
        }

        private async Task<List<Domain.Entities.Playlist>> FetchPlaylists(Query query, CancellationToken ct)
        {
            var targetUser = await db.Users.FirstOrDefaultAsync(u => u.Username == query.Username, ct);
            if (targetUser is null)
                return [];

            var isUserOwner = query.RequestingUserId == targetUser.Id;

            var dbQuery = db.Playlists
                .Where(p => p.UserId == targetUser.Id && p.Scope == PlaylistScope.User);

            if (!isUserOwner)
            {
                dbQuery = dbQuery.Where(p => p.Visibility == PlaylistVisibility.Public);
            }

            if (!string.IsNullOrEmpty(query.Cursor) && DateTimeOffset.TryParse(query.Cursor, out var cursorDate))
            {
                dbQuery = dbQuery.Where(p => p.CreatedAt < cursorDate);
            }

            return await dbQuery
                .OrderByDescending(p => p.CreatedAt)
                .Take(query.Limit + 1)
                .ToListAsync(ct);
        }
    }
}
