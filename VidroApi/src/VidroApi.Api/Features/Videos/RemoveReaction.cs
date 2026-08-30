using System.Security.Claims;
using CSharpFunctionalExtensions;
using Mediator;
using Microsoft.EntityFrameworkCore;
using VidroApi.Api.Extensions;
using VidroApi.Domain.Entities;
using VidroApi.Domain.Enums;
using VidroApi.Domain.Errors;
using VidroApi.Domain.Errors.EntityErrors;
using VidroApi.Infrastructure.Persistence;

namespace VidroApi.Api.Features.Videos;

public static class RemoveReaction
{
    public record Command : IRequest<UnitResult<Error>>
    {
        public Guid VideoId { get; init; }
        public Guid UserId { get; init; }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/v1/videos/{videoId:guid}/react", async (
            Guid videoId,
            ClaimsPrincipal user,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var cmd = new Command { VideoId = videoId, UserId = user.GetUserId() };
            var result = await mediator.Send(cmd, ct);
            return result.ToApiResult();
        })
        .RequireAuthorization();

    public class Handler(AppDbContext db)
        : IRequestHandler<Command, UnitResult<Error>>
    {
        public async ValueTask<UnitResult<Error>> Handle(Command cmd, CancellationToken ct)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var reaction = await FetchReaction(cmd.VideoId, cmd.UserId, ct);
            if (reaction is null)
                return Errors.Reaction.NotFound();

            var deletedCount = await db.Reactions
                .Where(r => r.Id == reaction.Id)
                .ExecuteDeleteAsync(ct);

            if (deletedCount > 0)
                await DecrementCounter(cmd.VideoId, reaction.Type, ct);

            await tx.CommitAsync(ct);

            return UnitResult.Success<Error>();
        }

        private Task<Reaction?> FetchReaction(Guid videoId, Guid userId, CancellationToken ct)
        {
            return db.Reactions.FirstOrDefaultAsync(
                r => r.VideoId == videoId && r.UserId == userId, ct);
        }

        private Task<int> DecrementCounter(Guid videoId, ReactionType type, CancellationToken ct)
        {
            if (type == ReactionType.Like)
                return db.Videos
                    .Where(v => v.Id == videoId)
                    .ExecuteUpdateAsync(s => s.SetProperty(v => v.LikeCount, v => v.LikeCount - 1), ct);

            return db.Videos
                .Where(v => v.Id == videoId)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.DislikeCount, v => v.DislikeCount - 1), ct);
        }
    }
}
