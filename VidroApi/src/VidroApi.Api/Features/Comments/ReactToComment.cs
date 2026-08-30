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

namespace VidroApi.Api.Features.Comments;

public static class ReactToComment
{
    public record Request
    {
        public ReactionType Type { get; init; }
    }

    public record Command : IRequest<UnitResult<Error>>
    {
        public Guid CommentId { get; init; }
        public Guid UserId { get; init; }
        public ReactionType Type { get; init; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(r => r.Type).IsInEnum();
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/v1/comments/{commentId:guid}/reactions", async (
            Guid commentId,
            Request req,
            ClaimsPrincipal user,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var cmd = new Command { CommentId = commentId, UserId = user.GetUserId(), Type = req.Type };
            var result = await mediator.Send(cmd, ct);
            return result.ToApiResult();
        })
        .RequireAuthorization();

    public class Handler(AppDbContext db, IDateTimeProvider clock)
        : IRequestHandler<Command, UnitResult<Error>>
    {
        public async ValueTask<UnitResult<Error>> Handle(Command cmd, CancellationToken ct)
        {
            var commentExists = await db.Comments.AnyAsync(
                c => c.Id == cmd.CommentId && !c.IsDeleted, ct);

            if (!commentExists)
                return Errors.Comment.NotFound(cmd.CommentId);

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var existingReaction = await FetchReaction(cmd.CommentId, cmd.UserId, ct);

            if (existingReaction is null)
            {
                var newReaction = new CommentReaction(cmd.CommentId, cmd.UserId, cmd.Type, clock.UtcNow);
                db.CommentReactions.Add(newReaction);
                await db.SaveChangesAsync(ct);
                await IncrementCounter(cmd.CommentId, cmd.Type, ct);
            }
            else if (existingReaction.Type != cmd.Type)
            {
                existingReaction.ChangeType(cmd.Type);
                await db.SaveChangesAsync(ct);
                await SwapCounters(cmd.CommentId, cmd.Type, ct);
            }

            await tx.CommitAsync(ct);

            return UnitResult.Success<Error>();
        }

        private Task<CommentReaction?> FetchReaction(Guid commentId, Guid userId, CancellationToken ct)
        {
            return db.CommentReactions.FirstOrDefaultAsync(
                r => r.CommentId == commentId && r.UserId == userId, ct);
        }

        private Task<int> IncrementCounter(Guid commentId, ReactionType type, CancellationToken ct)
        {
            if (type == ReactionType.Like)
                return db.Comments
                    .Where(c => c.Id == commentId)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.LikeCount, c => c.LikeCount + 1), ct);

            return db.Comments
                .Where(c => c.Id == commentId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.DislikeCount, c => c.DislikeCount + 1), ct);
        }

        private Task<int> SwapCounters(Guid commentId, ReactionType newType, CancellationToken ct)
        {
            if (newType == ReactionType.Like)
                return db.Comments
                    .Where(c => c.Id == commentId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(c => c.LikeCount, c => c.LikeCount + 1)
                        .SetProperty(c => c.DislikeCount, c => c.DislikeCount - 1),
                        ct);

            return db.Comments
                .Where(c => c.Id == commentId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.LikeCount, c => c.LikeCount - 1)
                    .SetProperty(c => c.DislikeCount, c => c.DislikeCount + 1),
                    ct);
        }
    }
}
