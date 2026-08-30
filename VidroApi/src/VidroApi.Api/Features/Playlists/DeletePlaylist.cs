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

public static class DeletePlaylist
{
    public record Command : IRequest<UnitResult<Error>>
    {
        public Guid PlaylistId { get; init; }
        public Guid UserId { get; init; }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/v1/playlists/{playlistId:guid}", async (
            Guid playlistId,
            ClaimsPrincipal user,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var cmd = new Command { PlaylistId = playlistId, UserId = user.GetUserId() };
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

            db.Playlists.Remove(playlist);
            await db.SaveChangesAsync(ct);

            return UnitResult.Success<Error>();
        }
    }
}
