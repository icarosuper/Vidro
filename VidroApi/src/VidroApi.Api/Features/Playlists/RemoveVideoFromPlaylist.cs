using System.Security.Claims;
using CSharpFunctionalExtensions;
using Mediator;
using Microsoft.EntityFrameworkCore;
using VidroApi.Api.Extensions;
using VidroApi.Domain.Entities;
using VidroApi.Domain.Errors;
using VidroApi.Domain.Errors.EntityErrors;
using VidroApi.Infrastructure.Persistence;

namespace VidroApi.Api.Features.Playlists;

public static class RemoveVideoFromPlaylist
{
    public record Command : IRequest<UnitResult<Error>>
    {
        public Guid PlaylistId { get; init; }
        public Guid UserId { get; init; }
        public Guid VideoId { get; init; }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/v1/playlists/{playlistId:guid}/items/{videoId:guid}", async (
            Guid playlistId,
            Guid videoId,
            ClaimsPrincipal user,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var cmd = new Command
            {
                PlaylistId = playlistId,
                UserId = user.GetUserId(),
                VideoId = videoId
            };
            var result = await mediator.Send(cmd, ct);
            return result.ToApiResult();
        })
        .RequireAuthorization();

    public class Handler(AppDbContext db) : IRequestHandler<Command, UnitResult<Error>>
    {
        public async ValueTask<UnitResult<Error>> Handle(Command cmd, CancellationToken ct)
        {
            var playlist = await db.Playlists.FirstOrDefaultAsync(p => p.Id == cmd.PlaylistId, ct);

            if (playlist is null)
                return CommonErrors.NotFound(nameof(Playlist), cmd.PlaylistId);

            var userIsNotOwner = playlist.UserId != cmd.UserId;
            if (userIsNotOwner)
                return playlist.IsPrivate
                    ? CommonErrors.NotFound(nameof(Playlist), cmd.PlaylistId)
                    : Errors.Playlist.NotOwner();

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var deletedCount = await db.PlaylistItems
                .Where(pi => pi.PlaylistId == cmd.PlaylistId && pi.VideoId == cmd.VideoId)
                .ExecuteDeleteAsync(ct);

            var videoNotFound = deletedCount == 0;
            if (videoNotFound)
                return Errors.Playlist.VideoNotInPlaylist();

            await DecrementVideoCount(cmd.PlaylistId, ct);

            await tx.CommitAsync(ct);

            return UnitResult.Success<Error>();
        }

        private Task<int> DecrementVideoCount(Guid playlistId, CancellationToken ct)
        {
            return db.Playlists
                .Where(p => p.Id == playlistId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.VideoCount, p => p.VideoCount - 1), ct);
        }
    }
}
