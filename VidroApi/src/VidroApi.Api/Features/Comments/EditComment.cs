using System.Security.Claims;
using CSharpFunctionalExtensions;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using VidroApi.Api.Extensions;
using VidroApi.Application.Abstractions;
using VidroApi.Domain.Entities;
using VidroApi.Domain.Errors;
using VidroApi.Domain.Errors.EntityErrors;
using VidroApi.Infrastructure.Persistence;

namespace VidroApi.Api.Features.Comments;

public static class EditComment
{
    public record Request
    {
        public string Content { get; init; } = null!;
    }

    public record Command : IRequest<UnitResult<Error>>
    {
        public Guid CommentId { get; init; }
        public Guid UserId { get; init; }
        public string Content { get; init; } = null!;
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
        app.MapPut("/v1/comments/{commentId:guid}", async (
            Guid commentId,
            Request req,
            ClaimsPrincipal user,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var cmd = new Command
            {
                CommentId = commentId,
                UserId = user.GetUserId(),
                Content = req.Content
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
            var comment = await FetchActiveComment(cmd.CommentId, ct);
            if (comment is null)
                return Errors.Comment.NotFound(cmd.CommentId);

            var isOwner = comment.UserId == cmd.UserId;
            if (!isOwner)
                return Errors.Comment.NotOwner();

            comment.Edit(cmd.Content, clock.UtcNow);
            await db.SaveChangesAsync(ct);

            return UnitResult.Success<Error>();
        }

        private Task<Comment?> FetchActiveComment(Guid commentId, CancellationToken ct)
        {
            return db.Comments.FirstOrDefaultAsync(c => c.Id == commentId && !c.IsDeleted, ct);
        }
    }
}
