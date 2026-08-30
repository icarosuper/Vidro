using System.Security.Claims;
using CSharpFunctionalExtensions;
using Mediator;
using Microsoft.EntityFrameworkCore;
using VidroApi.Api.Extensions;
using VidroApi.Domain.Enums;
using VidroApi.Domain.Errors;
using VidroApi.Domain.Errors.EntityErrors;
using VidroApi.Infrastructure.Persistence;

namespace VidroApi.Api.Features.Comments;

public static class RemoveCommentReaction
{
    public record Command : IRequest<UnitResult<Error>>
    {
        public Guid CommentId { get; init; }
        public Guid UserId { get; init; }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/v1/comments/{commentId:guid}/reactions", async (
            Guid commentId,
            ClaimsPrincipal user,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var cmd = new Command { CommentId = commentId, UserId = user.GetUserId() };
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

            var reaction = await FetchReaction(cmd.CommentId, cmd.UserId, ct);
            if (reaction is null)
                return Errors.CommentReaction.NotFound();

            var deletedCount = await db.CommentReactions
                .Where(r => r.Id == reaction.Id)
                .ExecuteDeleteAsync(ct);

            if (deletedCount > 0)
                await DecrementCounter(cmd.CommentId, reaction.Type, ct);

            await tx.CommitAsync(ct);

            return UnitResult.Success<Error>();
        }

        private Task<Domain.Entities.CommentReaction?> FetchReaction(Guid commentId, Guid userId, CancellationToken ct)
        {
            return db.CommentReactions.FirstOrDefaultAsync(
                r => r.CommentId == commentId && r.UserId == userId, ct);
        }

        private Task<int> DecrementCounter(Guid commentId, ReactionType type, CancellationToken ct)
        {
            if (type == ReactionType.Like)
                return db.Comments
                    .Where(c => c.Id == commentId)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.LikeCount, c => c.LikeCount - 1), ct);

            return db.Comments
                .Where(c => c.Id == commentId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.DislikeCount, c => c.DislikeCount - 1), ct);
        }
    }
}
