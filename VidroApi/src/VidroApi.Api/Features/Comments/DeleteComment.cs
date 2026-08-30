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

namespace VidroApi.Api.Features.Comments;

public static class DeleteComment
{
    public record Command : IRequest<UnitResult<Error>>
    {
        public Guid CommentId { get; init; }
        public Guid UserId { get; init; }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/v1/comments/{commentId:guid}", async (
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

    public class Handler(AppDbContext db, IDateTimeProvider clock)
        : IRequestHandler<Command, UnitResult<Error>>
    {
        public async ValueTask<UnitResult<Error>> Handle(Command cmd, CancellationToken ct)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var comment = await FetchActiveComment(cmd.CommentId, ct);
            if (comment is null)
                return Errors.Comment.NotFound(cmd.CommentId);

            var isOwner = comment.UserId == cmd.UserId;
            if (!isOwner)
                return Errors.Comment.NotOwner();

            var softDeletedCount = await SoftDeleteComment(cmd.CommentId, ct);

            if (softDeletedCount > 0)
            {
                await DecrementVideoCommentCount(comment.VideoId, ct);

                if (comment.ParentCommentId.HasValue)
                    await DecrementParentReplyCount(comment.ParentCommentId.Value, ct);
            }

            await tx.CommitAsync(ct);

            return UnitResult.Success<Error>();
        }

        private Task<Comment?> FetchActiveComment(Guid commentId, CancellationToken ct)
        {
            return db.Comments.FirstOrDefaultAsync(c => c.Id == commentId && !c.IsDeleted, ct);
        }

        private Task<int> SoftDeleteComment(Guid commentId, CancellationToken ct)
        {
            return db.Comments
                .Where(c => c.Id == commentId && !c.IsDeleted)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.IsDeleted, true)
                    .SetProperty(c => c.UpdatedAt, clock.UtcNow), ct);
        }

        private Task<int> DecrementVideoCommentCount(Guid videoId, CancellationToken ct)
        {
            return db.Videos
                .Where(v => v.Id == videoId)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.CommentCount, v => v.CommentCount - 1), ct);
        }

        private Task<int> DecrementParentReplyCount(Guid parentId, CancellationToken ct)
        {
            return db.Comments
                .Where(c => c.Id == parentId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ReplyCount, c => c.ReplyCount - 1), ct);
        }
    }
}
