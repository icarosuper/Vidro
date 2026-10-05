using CSharpFunctionalExtensions;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VidroApi.Api.Middleware;
using VidroApi.Application.Abstractions;
using VidroApi.Domain.Enums;
using VidroApi.Domain.Errors;
using VidroApi.Domain.Errors.EntityErrors;
using VidroApi.Infrastructure.Persistence;
using VidroApi.Infrastructure.Settings;

namespace VidroApi.Api.Features.Videos;

public static class MinioUploadCompleted
{
    public record Request
    {
        public string EventName { get; init; } = null!;
        public string Key { get; init; } = null!;
    }

    public record Command : IRequest<UnitResult<Error>>
    {
        public Guid VideoId { get; init; }
        public string CorrelationId { get; init; } = null!;
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/webhooks/minio-upload-completed", async (
            Request req,
            HttpContext ctx,
            IMediator mediator,
            IOptions<WebhookSettings> webhookOptions,
            CancellationToken ct) =>
        {
            var authHeader = ctx.Request.Headers.Authorization.ToString();
            var expectedToken = $"Bearer {webhookOptions.Value.MinioUploadToken}";
            var tokenIsInvalid = authHeader != expectedToken;
            if (tokenIsInvalid)
                return Results.Unauthorized();

            var isUploadEvent = req.EventName == "s3:ObjectCreated:Put";
            if (!isUploadEvent)
                return Results.Ok();

            var videoId = ParseVideoIdFromKey(req.Key);
            if (videoId is null)
                return Results.Ok();

            // The ID of *this* request is what the whole job will be logged under, worker
            // included — MinIO does not send one, so it is the one the middleware generated.
            var correlationId = CorrelationIdMiddleware.GetCorrelationId(ctx);

            await mediator.Send(
                new Command { VideoId = videoId.Value, CorrelationId = correlationId },
                ct);
            return Results.Ok();
        });

    private static Guid? ParseVideoIdFromKey(string key)
    {
        // Key format: "{bucket}/raw/{videoId}"
        var segments = key.Split('/');
        var rawSegmentIndex = Array.IndexOf(segments, "raw");
        if (rawSegmentIndex < 0 || rawSegmentIndex + 1 >= segments.Length)
            return null;

        var videoIdSegment = segments[rawSegmentIndex + 1];
        return Guid.TryParse(videoIdSegment, out var videoId) ? videoId : null;
    }

    public class Handler(AppDbContext db, IJobQueueService jobQueue, IDateTimeProvider clock, IOptions<ApiSettings> apiOptions)
        : IRequestHandler<Command, UnitResult<Error>>
    {
        public async ValueTask<UnitResult<Error>> Handle(Command cmd, CancellationToken ct)
        {
            var video = await db.Videos.FirstOrDefaultAsync(v => v.Id == cmd.VideoId, ct);
            if (video is null)
                return CommonErrors.NotFound(nameof(Domain.Entities.Video), cmd.VideoId);

            var isPendingUpload = video.Status == VideoStatus.PendingUpload;
            if (!isPendingUpload)
                return Errors.Video.NotPendingUpload();

            video.MarkAsProcessing(clock.UtcNow);

            // Save first, publish second, commit last. A save that fails now fails before any job
            // exists — published first, the job would run for a video still PendingUpload and its
            // webhook would be ignored. A publish that fails (Redis down) rolls the save back, so
            // the video stays PendingUpload and the upload reconciliation republishes it once
            // UploadExpiresAt passes. Only a failing commit after a good publish is left uncovered.
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.SaveChangesAsync(ct);

            var callbackUrl = $"{apiOptions.Value.BaseUrl}/webhooks/video-processed";
            await jobQueue.PublishJobAsync(cmd.VideoId.ToString(), callbackUrl, cmd.CorrelationId, ct);

            await tx.CommitAsync(ct);

            return UnitResult.Success<Error>();
        }
    }
}
