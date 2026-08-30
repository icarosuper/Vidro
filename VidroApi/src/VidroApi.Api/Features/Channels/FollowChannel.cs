using System.Security.Claims;
using CSharpFunctionalExtensions;
using Mediator;
using Microsoft.EntityFrameworkCore;
using VidroApi.Api.Extensions;
using VidroApi.Application.Abstractions;
using VidroApi.Domain.Entities;
using VidroApi.Domain.Errors;
using VidroApi.Domain.Errors.EntityErrors;
using VidroApi.Infrastructure.Persistence;

namespace VidroApi.Api.Features.Channels;

public static class FollowChannel
{
    public record Command : IRequest<UnitResult<Error>>
    {
        public string Username { get; init; } = null!;
        public string Handle { get; init; } = null!;
        public Guid UserId { get; init; }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/v1/users/{username}/channels/{handle}/follow", async (
            string username,
            string handle,
            ClaimsPrincipal user,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var cmd = new Command
            {
                Username = username,
                Handle = handle,
                UserId = user.GetUserId()
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
            var channel = await db.Channels
                .FirstOrDefaultAsync(c => c.Handle == cmd.Handle && c.User.Username == cmd.Username, ct);
            if (channel is null)
                return CommonErrors.NotFound(nameof(Channel), $"{cmd.Username}/{cmd.Handle}");

            var isOwnChannel = channel.UserId == cmd.UserId;
            if (isOwnChannel)
                return Errors.Channel.CannotFollowOwnChannel();

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var alreadyFollowing = await db.ChannelFollowers
                .AnyAsync(cf => cf.ChannelId == channel.Id && cf.UserId == cmd.UserId, ct);
            if (alreadyFollowing)
                return Errors.Channel.AlreadyFollowing();

            db.ChannelFollowers.Add(new ChannelFollower(channel.Id, cmd.UserId, clock.UtcNow));
            await db.SaveChangesAsync(ct);
            await IncrementFollowerCount(channel.Id, ct);

            await tx.CommitAsync(ct);

            return UnitResult.Success<Error>();
        }

        private Task<int> IncrementFollowerCount(Guid channelId, CancellationToken ct)
        {
            return db.Channels
                .Where(c => c.Id == channelId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.FollowerCount, c => c.FollowerCount + 1), ct);
        }
    }
}
