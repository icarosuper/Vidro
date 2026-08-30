using System.Security.Claims;
using CSharpFunctionalExtensions;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VidroApi.Api.Common;
using VidroApi.Api.Extensions;
using VidroApi.Domain.Entities;
using VidroApi.Domain.Enums;
using VidroApi.Domain.Errors;
using VidroApi.Infrastructure.Persistence;
using VidroApi.Infrastructure.Settings;

namespace VidroApi.Api.Features.Comments;

public static class ListComments
{
    public enum CommentSortOrder { Recent, Popular }

    public record Command : IRequest<Result<Response, Error>>
    {
        public Guid VideoId { get; init; }
        public Guid? RequestingUserId { get; init; }
        public CommentSortOrder Sort { get; init; }
        public int Limit { get; init; }
        public DateTimeOffset? Cursor { get; init; }
    }

    public record Response
    {
        public List<CommentSummary> Comments { get; init; } = [];
        public DateTimeOffset? NextCursor { get; init; }

        public record CommentSummary
        {
            public Guid CommentId { get; init; }
            public Guid UserId { get; init; }
            public string Username { get; init; } = null!;
            public string? Content { get; init; }
            public bool IsDeleted { get; init; }
            public int LikeCount { get; init; }
            public int DislikeCount { get; init; }
            public int ReplyCount { get; init; }
            public EnumValue? UserReaction { get; init; }
            public DateTimeOffset CreatedAt { get; init; }
            public DateTimeOffset? UpdatedAt { get; init; }
        }
    }

    public class Validator : AbstractValidator<Command>
    {
        public Validator(IOptions<ListCommentsSettings> options)
        {
            RuleFor(x => x.Sort).IsInEnum();

            RuleFor(x => x.Limit)
                .InclusiveBetween(1, options.Value.MaxLimit)
                .WithMessage(x => $"Limit must be between 1 and {options.Value.MaxLimit}.")
                .When(x => x.Sort == CommentSortOrder.Recent);

            RuleFor(x => x.Limit)
                .InclusiveBetween(1, options.Value.MaxPopularLimit)
                .WithMessage(x => $"Limit must be between 1 and {options.Value.MaxPopularLimit}.")
                .When(x => x.Sort == CommentSortOrder.Popular);
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/v1/videos/{videoId:guid}/comments", async (
            Guid videoId,
            ClaimsPrincipal user,
            IMediator mediator,
            CommentSortOrder sort,
            int limit,
            DateTimeOffset? cursor,
            CancellationToken ct) =>
        {
            Guid? requestingUserId = user.Identity?.IsAuthenticated == true
                ? user.GetUserId()
                : null;
            var cmd = new Command
            {
                VideoId = videoId,
                RequestingUserId = requestingUserId,
                Sort = sort,
                Limit = limit,
                Cursor = cursor
            };
            var result = await mediator.Send(cmd, ct);
            return result.ToApiResult(StatusCodes.Status200OK);
        });

    public class Handler(AppDbContext db)
        : IRequestHandler<Command, Result<Response, Error>>
    {
        public async ValueTask<Result<Response, Error>> Handle(Command cmd, CancellationToken ct)
        {
            var videoAccessible = await IsVideoAccessible(cmd, ct);
            if (!videoAccessible)
                return CommonErrors.NotFound(nameof(Video), cmd.VideoId);

            if (cmd.Sort == CommentSortOrder.Popular)
            {
                var popularComments = await FetchPopularComments(cmd.VideoId, cmd.RequestingUserId, cmd.Limit, ct);
                return new Response
                {
                    Comments = popularComments
                };
            }

            var recentComments = await FetchRecentComments(cmd.VideoId, cmd.RequestingUserId, cmd.Cursor, cmd.Limit, ct);

            var nextCursor = recentComments.Count == cmd.Limit
                ? recentComments[^1].CreatedAt
                : (DateTimeOffset?)null;

            return new Response
            {
                Comments = recentComments,
                NextCursor = nextCursor
            };
        }

        private Task<bool> IsVideoAccessible(Command cmd, CancellationToken ct)
        {
            return db.Videos.AnyAsync(
                v => v.Id == cmd.VideoId
                     && v.Status == VideoStatus.Ready
                     && (v.Visibility == VideoVisibility.Public || v.Channel.UserId == cmd.RequestingUserId),
                ct);
        }

        private Task<List<Response.CommentSummary>> FetchRecentComments(
            Guid videoId, Guid? requestingUserId, DateTimeOffset? cursor, int limit, CancellationToken ct)
        {
            var query = db.Comments.Where(c => c.VideoId == videoId && c.ParentCommentId == null);

            if (cursor.HasValue)
                query = query.Where(c => c.CreatedAt < cursor.Value);

            return query
                .OrderByDescending(c => c.CreatedAt)
                .Take(limit)
                .Select(c => new Response.CommentSummary
                {
                    CommentId = c.Id,
                    UserId = c.UserId,
                    Username = c.User.Username,
                    Content = c.IsDeleted ? null : c.Content,
                    IsDeleted = c.IsDeleted,
                    LikeCount = c.LikeCount,
                    DislikeCount = c.DislikeCount,
                    ReplyCount = c.ReplyCount,
                    UserReaction = db.CommentReactions
                        .Where(cr => cr.CommentId == c.Id && cr.UserId == requestingUserId)
                        .Select(cr => new EnumValue { Id = (int)cr.Type, Value = cr.Type.ToString() })
                        .FirstOrDefault(),
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt
                })
                .ToListAsync(ct);
        }

        private Task<List<Response.CommentSummary>> FetchPopularComments(
            Guid videoId, Guid? requestingUserId, int limit, CancellationToken ct)
        {
            return db.Comments
                .Where(c => c.VideoId == videoId && c.ParentCommentId == null)
                .OrderByDescending(c => c.LikeCount)
                .ThenByDescending(c => c.CreatedAt)
                .Take(limit)
                .Select(c => new Response.CommentSummary
                {
                    CommentId = c.Id,
                    UserId = c.UserId,
                    Username = c.User.Username,
                    Content = c.IsDeleted ? null : c.Content,
                    IsDeleted = c.IsDeleted,
                    LikeCount = c.LikeCount,
                    DislikeCount = c.DislikeCount,
                    ReplyCount = c.ReplyCount,
                    UserReaction = db.CommentReactions
                        .Where(cr => cr.CommentId == c.Id && cr.UserId == requestingUserId)
                        .Select(cr => new EnumValue { Id = (int)cr.Type, Value = cr.Type.ToString() })
                        .FirstOrDefault(),
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt
                })
                .ToListAsync(ct);
        }
    }
}
