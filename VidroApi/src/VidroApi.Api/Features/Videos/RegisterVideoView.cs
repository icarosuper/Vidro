using System.Net;
using System.Security.Claims;
using CSharpFunctionalExtensions;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using VidroApi.Api.Extensions;
using VidroApi.Domain.Enums;
using VidroApi.Domain.Errors;
using VidroApi.Infrastructure.Persistence;
using VidroApi.Infrastructure.Settings;

namespace VidroApi.Api.Features.Videos;

public static class RegisterVideoView
{
    public record Command : IRequest<UnitResult<Error>>
    {
        public Guid VideoId { get; init; }
        public Guid? RequestingUserId { get; init; }
        public string IpAddress { get; init; } = null!;
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/v1/videos/{videoId:guid}/view", async (
            Guid videoId,
            ClaimsPrincipal user,
            HttpContext httpContext,
            IMediator mediator,
            CancellationToken ct) =>
        {
            Guid? requestingUserId = user.Identity?.IsAuthenticated == true
                ? user.GetUserId()
                : null;
            var ipAddress = httpContext.Connection.RemoteIpAddress ?? IPAddress.None;
            var cmd = new Command
            {
                VideoId = videoId,
                RequestingUserId = requestingUserId,
                IpAddress = ipAddress.ToString()
            };
            var result = await mediator.Send(cmd, ct);
            return result.ToApiResult();
        });

    public class Handler(AppDbContext db, IConnectionMultiplexer redis, IOptions<VideoSettings> videoOptions)
        : IRequestHandler<Command, UnitResult<Error>>
    {
        private readonly TimeSpan _dedupWindow = TimeSpan.FromHours(videoOptions.Value.ViewDeduplicationWindowHours);

        public async ValueTask<UnitResult<Error>> Handle(Command cmd, CancellationToken ct)
        {
            var videoExists = await db.Videos
                .AnyAsync(v => v.Id == cmd.VideoId
                    && (v.Channel.UserId == cmd.RequestingUserId
                        || (v.Status == VideoStatus.Ready && v.Visibility != VideoVisibility.Private)),
                    ct);

            if (!videoExists)
                return CommonErrors.NotFound(nameof(Domain.Entities.Video), cmd.VideoId);

            var isNewView = await RegisterDeduplicationKey(cmd);
            if (!isNewView)
                return UnitResult.Success<Error>();

            await IncrementViewCount(cmd.VideoId, ct);

            return UnitResult.Success<Error>();
        }

        private Task<bool> RegisterDeduplicationKey(Command cmd)
        {
            var dedupIdentifier = cmd.RequestingUserId.HasValue
                ? cmd.RequestingUserId.Value.ToString()
                : cmd.IpAddress;
            var dedupKey = $"view:{cmd.VideoId}:{dedupIdentifier}";
            return redis.GetDatabase().StringSetAsync(dedupKey, "1", _dedupWindow, When.NotExists);
        }

        private Task<int> IncrementViewCount(Guid videoId, CancellationToken ct)
        {
            return db.Videos
                .Where(v => v.Id == videoId)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.ViewCount, v => v.ViewCount + 1), ct);
        }
    }
}
