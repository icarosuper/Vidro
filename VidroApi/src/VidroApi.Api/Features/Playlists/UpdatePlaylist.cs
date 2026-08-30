using System.Security.Claims;
using CSharpFunctionalExtensions;
using FluentValidation;
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

public static class UpdatePlaylist
{
    public record Request
    {
        public string Name { get; init; } = null!;
        public string? Description { get; init; }
        public PlaylistVisibility Visibility { get; init; }
    }

    public record Command : IRequest<UnitResult<Error>>
    {
        public Guid PlaylistId { get; init; }
        public Guid UserId { get; init; }
        public string Name { get; init; } = null!;
        public string? Description { get; init; }
        public PlaylistVisibility Visibility { get; init; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(x => x.Name)
                .NotEmpty()
                .MaximumLength(Playlist.NameMaxLength);

            RuleFor(x => x.Description)
                .MaximumLength(Playlist.DescriptionMaxLength)
                .When(x => x.Description is not null);

            RuleFor(x => x.Visibility).IsInEnum();
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/v1/playlists/{playlistId:guid}", async (
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
                Name = req.Name,
                Description = req.Description,
                Visibility = req.Visibility
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

            playlist.UpdateDetails(cmd.Name, cmd.Description, cmd.Visibility, clock.UtcNow);
            await db.SaveChangesAsync(ct);

            return UnitResult.Success<Error>();
        }
    }
}
