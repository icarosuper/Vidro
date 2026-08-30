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

public static class AddComment
{
    public record Request
    {
        public string Content { get; init; } = null!;
        public Guid? ParentCommentId { get; init; }
    }

    public record Command : IRequest<Result<Response, Error>>
    {
        public Guid VideoId { get; init; }
        public Guid UserId { get; init; }
        public string Content { get; init; } = null!;
        public Guid? ParentCommentId { get; init; }
    }

    public record Response
    {
        public Guid CommentId { get; init; }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(r => r.Content)
                .NotEmpty()
                .MaximumLength(Comment.ContentMaxLength);
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/v1/videos/{videoId:guid}/comments", async (
            Guid videoId,
            Request req,
            ClaimsPrincipal user,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var cmd = new Command
            {
                VideoId = videoId,
                UserId = user.GetUserId(),
                Content = req.Content,
                ParentCommentId = req.ParentCommentId
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
            var video = await FetchAccessibleVideo(cmd.VideoId, cmd.UserId, ct);
            if (video is null)
                return CommonErrors.NotFound(nameof(Video), cmd.VideoId);

            if (cmd.ParentCommentId.HasValue)
            {
                var parentValidation = await ValidateParentComment(cmd.ParentCommentId.Value, cmd.VideoId, ct);
                if (parentValidation.IsFailure)
                    return parentValidation.Error;
            }

            var comment = new Comment(cmd.VideoId, cmd.UserId, cmd.Content, cmd.ParentCommentId, clock.UtcNow);
            db.Comments.Add(comment);

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            await db.SaveChangesAsync(ct);
            await IncrementVideoCommentCount(cmd.VideoId, ct);

            if (cmd.ParentCommentId.HasValue)
                await IncrementParentReplyCount(cmd.ParentCommentId.Value, ct);

            await tx.CommitAsync(ct);

            return new Response
            {
                CommentId = comment.Id
            };
        }

        private Task<Video?> FetchAccessibleVideo(Guid videoId, Guid userId, CancellationToken ct)
        {
            return db.Videos
                .Include(v => v.Channel)
                .FirstOrDefaultAsync(v => v.Id == videoId
                    && v.Status == VideoStatus.Ready
                    && (v.Visibility == VideoVisibility.Public || v.Channel.UserId == userId),
                    ct);
        }

        private async Task<UnitResult<Error>> ValidateParentComment(Guid parentId, Guid videoId, CancellationToken ct)
        {
            var parent = await db.Comments.FirstOrDefaultAsync(
                c => c.Id == parentId && !c.IsDeleted, ct);

            if (parent is null)
                return Errors.Comment.ParentNotFound(parentId);

            if (parent.ParentCommentId is not null)
                return Errors.Comment.ReplyNestingNotAllowed();

            if (parent.VideoId != videoId)
                return Errors.Comment.ParentVideoMismatch();

            return UnitResult.Success<Error>();
        }

        private Task<int> IncrementVideoCommentCount(Guid videoId, CancellationToken ct)
        {
            return db.Videos
                .Where(v => v.Id == videoId)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.CommentCount, v => v.CommentCount + 1), ct);
        }

        private Task<int> IncrementParentReplyCount(Guid parentId, CancellationToken ct)
        {
            return db.Comments
                .Where(c => c.Id == parentId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ReplyCount, c => c.ReplyCount + 1), ct);
        }
    }
}
