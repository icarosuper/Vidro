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

namespace VidroApi.Api.Features.Videos;

public static class ReactToVideo
{
    public record Request
    {
        public ReactionType Type { get; init; }
    }

    public record Command : IRequest<UnitResult<Error>>
    {
        public Guid VideoId { get; init; }
        public Guid UserId { get; init; }
        public ReactionType Type { get; init; }
    }

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Type).IsInEnum();
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/v1/videos/{videoId:guid}/react", async (
            Guid videoId,
            Request req,
            ClaimsPrincipal user,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var cmd = new Command { VideoId = videoId, UserId = user.GetUserId(), Type = req.Type };
            var result = await mediator.Send(cmd, ct);
            return result.ToApiResult();
        })
        .RequireAuthorization();

    public class Handler(AppDbContext db, IDateTimeProvider clock)
        : IRequestHandler<Command, UnitResult<Error>>
    {
        public async ValueTask<UnitResult<Error>> Handle(Command cmd, CancellationToken ct)
        {
            var video = await FetchAccessibleVideo(cmd.VideoId, cmd.UserId, ct);
            if (video is null)
                return CommonErrors.NotFound(nameof(Video), cmd.VideoId);

            var isOwner = video.Channel.UserId == cmd.UserId;
            if (isOwner)
                return Errors.Video.CannotReactToOwnVideo();

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var existingReaction = await FetchReaction(cmd.VideoId, cmd.UserId, ct);

            if (existingReaction is null)
            {
                var newReaction = new Reaction(cmd.VideoId, cmd.UserId, cmd.Type, clock.UtcNow);
                db.Reactions.Add(newReaction);
                await db.SaveChangesAsync(ct);
                await IncrementCounter(cmd.VideoId, cmd.Type, ct);
            }
            else if (existingReaction.Type != cmd.Type)
            {
                db.Reactions.Remove(existingReaction);
                var newReaction = new Reaction(cmd.VideoId, cmd.UserId, cmd.Type, clock.UtcNow);
                db.Reactions.Add(newReaction);
                await db.SaveChangesAsync(ct);
                await SwapCounters(cmd.VideoId, cmd.Type, ct);
            }

            await tx.CommitAsync(ct);

            return UnitResult.Success<Error>();
        }

        private Task<Video?> FetchAccessibleVideo(Guid videoId, Guid userId, CancellationToken ct)
        {
            return db.Videos
                .Include(v => v.Channel)
                .FirstOrDefaultAsync(v => v.Id == videoId
                    && (v.Channel.UserId == userId
                        || (v.Status == VideoStatus.Ready && v.Visibility != VideoVisibility.Private)),
                    ct);
        }

        private Task<Reaction?> FetchReaction(Guid videoId, Guid userId, CancellationToken ct)
        {
            return db.Reactions.FirstOrDefaultAsync(
                r => r.VideoId == videoId && r.UserId == userId, ct);
        }

        private Task<int> IncrementCounter(Guid videoId, ReactionType type, CancellationToken ct)
        {
            if (type == ReactionType.Like)
                return db.Videos
                    .Where(v => v.Id == videoId)
                    .ExecuteUpdateAsync(s => s.SetProperty(v => v.LikeCount, v => v.LikeCount + 1), ct);

            return db.Videos
                .Where(v => v.Id == videoId)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.DislikeCount, v => v.DislikeCount + 1), ct);
        }

        private Task<int> SwapCounters(Guid videoId, ReactionType newType, CancellationToken ct)
        {
            if (newType == ReactionType.Like)
                return db.Videos
                    .Where(v => v.Id == videoId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(v => v.LikeCount, v => v.LikeCount + 1)
                        .SetProperty(v => v.DislikeCount, v => v.DislikeCount - 1),
                        ct);

            return db.Videos
                .Where(v => v.Id == videoId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(v => v.LikeCount, v => v.LikeCount - 1)
                    .SetProperty(v => v.DislikeCount, v => v.DislikeCount + 1),
                    ct);
        }
    }
}
