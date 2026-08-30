using System.Security.Claims;
using CSharpFunctionalExtensions;
using Mediator;
using Microsoft.EntityFrameworkCore;
using VidroApi.Api.Extensions;
using VidroApi.Domain.Entities;
using VidroApi.Domain.Errors;
using VidroApi.Domain.Errors.EntityErrors;
using VidroApi.Infrastructure.Persistence;

namespace VidroApi.Api.Features.Channels;

public static class UnfollowChannel
{
    public record Command : IRequest<UnitResult<Error>>
    {
        public string Username { get; init; } = null!;
        public string Handle { get; init; } = null!;
        public Guid UserId { get; init; }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/v1/users/{username}/channels/{handle}/follow", async (
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

    public class Handler(AppDbContext db) : IRequestHandler<Command, UnitResult<Error>>
    {
        public async ValueTask<UnitResult<Error>> Handle(Command cmd, CancellationToken ct)
        {
            var channel = await db.Channels
                .FirstOrDefaultAsync(c => c.Handle == cmd.Handle && c.User.Username == cmd.Username, ct);
            if (channel is null)
                return CommonErrors.NotFound(nameof(Channel), $"{cmd.Username}/{cmd.Handle}");

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var deletedCount = await db.ChannelFollowers
                .Where(cf => cf.ChannelId == channel.Id && cf.UserId == cmd.UserId)
                .ExecuteDeleteAsync(ct);

            var notFollowing = deletedCount == 0;
            if (notFollowing)
                return Errors.Channel.NotFollowing();

            await DecrementFollowerCount(channel.Id, ct);

            await tx.CommitAsync(ct);

            return UnitResult.Success<Error>();
        }

        private Task<int> DecrementFollowerCount(Guid channelId, CancellationToken ct)
        {
            return db.Channels
                .Where(c => c.Id == channelId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.FollowerCount, c => c.FollowerCount - 1), ct);
        }
    }
}
