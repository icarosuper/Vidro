using System.Security.Claims;
using CSharpFunctionalExtensions;
using Mediator;
using Microsoft.EntityFrameworkCore;
using VidroApi.Api.Extensions;
using VidroApi.Application.Abstractions;
using VidroApi.Domain.Entities;
using VidroApi.Domain.Enums;
using VidroApi.Domain.Errors;
using VidroApi.Domain.Errors.EntityErrors;
using VidroApi.Infrastructure.Persistence;

namespace VidroApi.Api.Features.Playlists;

public static class AddVideoToPlaylist
{
    public record Request
    {
        public Guid VideoId { get; init; }
    }

    public record Command : IRequest<UnitResult<Error>>
    {
        public Guid PlaylistId { get; init; }
        public Guid UserId { get; init; }
        public Guid VideoId { get; init; }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/v1/playlists/{playlistId:guid}/items", async (
            Guid playlistId,
            Request req,
            ClaimsPrincipal user,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var cmd = new Command
            {
                PlaylistId = playlistId,
                UserId = user.GetUserId(),
                VideoId = req.VideoId
            };
            var result = await mediator.Send(cmd, ct);
            return result.ToApiResult();
        })
        .RequireAuthorization();

    public class Handler(AppDbContext db, IDateTimeProvider clock)
        : IRequestHandler<Command, UnitResult<Error>>
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

            var videoExists = await db.Videos.AnyAsync(v => v.Id == cmd.VideoId, ct);
            if (!videoExists)
                return CommonErrors.NotFound(nameof(Video), cmd.VideoId);

            if (playlist.Scope == PlaylistScope.Channel)
            {
                var videoFromChannel = await db.Videos
                    .AnyAsync(v => v.Id == cmd.VideoId && v.ChannelId == playlist.ChannelId, ct);
                if (!videoFromChannel)
                    return Errors.Playlist.VideoNotFromChannel();
            }

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var alreadyInPlaylist = await db.PlaylistItems
                .AnyAsync(pi => pi.PlaylistId == cmd.PlaylistId && pi.VideoId == cmd.VideoId, ct);
            if (alreadyInPlaylist)
                return Errors.Playlist.VideoAlreadyInPlaylist();

            db.PlaylistItems.Add(new PlaylistItem(cmd.PlaylistId, cmd.VideoId, clock.UtcNow));
            await db.SaveChangesAsync(ct);
            await IncrementVideoCount(cmd.PlaylistId, ct);

            await tx.CommitAsync(ct);

            return UnitResult.Success<Error>();
        }

        private Task<int> IncrementVideoCount(Guid playlistId, CancellationToken ct)
        {
            return db.Playlists
                .Where(p => p.Id == playlistId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.VideoCount, p => p.VideoCount + 1), ct);
        }
    }
}
