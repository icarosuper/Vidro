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
using VidroApi.Infrastructure.Persistence;

namespace VidroApi.Api.Features.Playlists;

public static class CreatePlaylist
{
    public record Request
    {
        public string Name { get; init; } = null!;
        public string? Description { get; init; }
        public PlaylistVisibility Visibility { get; init; }
        public PlaylistScope Scope { get; init; }
        public Guid? ChannelId { get; init; }
    }

    public record Command : IRequest<Result<Response, Error>>
    {
        public Guid UserId { get; init; }
        public string Name { get; init; } = null!;
        public string? Description { get; init; }
        public PlaylistVisibility Visibility { get; init; }
        public PlaylistScope Scope { get; init; }
        public Guid? ChannelId { get; init; }
    }

    public record Response
    {
        public Guid PlaylistId { get; init; }
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
            RuleFor(x => x.Scope).IsInEnum();

            RuleFor(x => x.ChannelId)
                .NotNull()
                .WithMessage("ChannelId is required when Scope is Channel.")
                .When(x => x.Scope == PlaylistScope.Channel);
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/v1/playlists", async (
            Request req,
            ClaimsPrincipal user,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var cmd = new Command
            {
                UserId = user.GetUserId(),
                Name = req.Name,
                Description = req.Description,
                Visibility = req.Visibility,
                Scope = req.Scope,
                ChannelId = req.ChannelId
            };
            var result = await mediator.Send(cmd, ct);
            return result.ToApiResult(StatusCodes.Status201Created);
        })
        .RequireAuthorization();

    public class Handler(AppDbContext db, IDateTimeProvider clock)
        : IRequestHandler<Command, Result<Response, Error>>
    {
        public async ValueTask<Result<Response, Error>> Handle(Command cmd, CancellationToken ct)
        {
            if (cmd.Scope == PlaylistScope.Channel)
            {
                var channelExists = await db.Channels
                    .AnyAsync(c => c.Id == cmd.ChannelId && c.UserId == cmd.UserId, ct);
                if (!channelExists)
                    return CommonErrors.NotFound(nameof(Channel), cmd.ChannelId!.Value);
            }

            var playlist = new Playlist(
                cmd.UserId,
                cmd.ChannelId,
                cmd.Name,
                cmd.Description,
                cmd.Scope,
                cmd.Visibility,
                clock.UtcNow);

            db.Playlists.Add(playlist);
            await db.SaveChangesAsync(ct);

            return new Response { PlaylistId = playlist.Id };
        }
    }
}
