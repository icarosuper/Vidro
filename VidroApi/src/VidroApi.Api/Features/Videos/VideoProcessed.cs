using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VidroApi.Application.Abstractions;
using VidroApi.Domain.Entities;
using VidroApi.Domain.Enums;
using VidroApi.Infrastructure.Persistence;
using VidroApi.Infrastructure.Settings;

namespace VidroApi.Api.Features.Videos;

public static class VideoProcessed
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    public record Command : IRequest
    {
        public Guid VideoId { get; init; }
        public bool Success { get; init; }
        public string? ProcessedPath { get; init; }
        public string? PreviewPath { get; init; }
        public string? HlsPath { get; init; }
        public string? AudioPath { get; init; }
        public List<string>? ThumbnailPaths { get; init; }
        public long? FileSizeBytes { get; init; }
        public double? DurationSeconds { get; init; }
        public int? Width { get; init; }
        public int? Height { get; init; }
        public string? Codec { get; init; }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/webhooks/video-processed", async (
            HttpContext ctx,
            IMediator mediator,
            IOptions<WebhookSettings> webhookOptions,
            CancellationToken ct) =>
        {
            var rawBody = await ReadRawBodyAsync(ctx, ct);

            var signatureHeader = ctx.Request.Headers["X-Webhook-Signature"].ToString();
            var signatureIsInvalid = !IsSignatureValid(rawBody, signatureHeader, webhookOptions.Value.Secret);
            if (signatureIsInvalid)
                return Results.Unauthorized();

            var cmd = TryDeserialize(rawBody);
            if (cmd is null)
                return Results.BadRequest();

            // 200 for everything that is not a malformed body: the worker retries any non-2xx
            // (webhook.go), and an unknown video or one no longer in Processing is a permanent
            // mismatch that a retry cannot fix. The handler logs why it ignored the payload.
            await mediator.Send(cmd, ct);
            return Results.Ok();
        });

    // A signed but malformed body is permanent: 400 rather than an unhandled JsonException (500).
    private static Command? TryDeserialize(byte[] rawBody)
    {
        try
        {
            return JsonSerializer.Deserialize<Command>(rawBody, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<byte[]> ReadRawBodyAsync(HttpContext ctx, CancellationToken ct)
    {
        ctx.Request.EnableBuffering();
        using var ms = new MemoryStream();
        await ctx.Request.Body.CopyToAsync(ms, ct);
        ctx.Request.Body.Position = 0;
        return ms.ToArray();
    }

    private static bool IsSignatureValid(byte[] payload, string signatureHeader, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var hash = HMACSHA256.HashData(keyBytes, payload);
        var expectedSignature = $"sha256={Convert.ToHexString(hash).ToLowerInvariant()}";
        var expectedBytes = Encoding.UTF8.GetBytes(expectedSignature);
        var actualBytes = Encoding.UTF8.GetBytes(signatureHeader);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    public class Handler(AppDbContext db, IDateTimeProvider clock, ILogger<Handler> logger)
        : IRequestHandler<Command>
    {
        public async ValueTask<Unit> Handle(Command cmd, CancellationToken ct)
        {
            var video = await db.Videos.FirstOrDefaultAsync(v => v.Id == cmd.VideoId, ct);
            if (video is null)
            {
                logger.LogWarning("Ignoring video-processed webhook for unknown video {VideoId}", cmd.VideoId);
                return Unit.Value;
            }

            var isProcessing = video.Status == VideoStatus.Processing;
            if (!isProcessing)
            {
                // The endpoint answers 200 anyway, so the worker will not retry: this line is the
                // only trace of a result that arrived too late — typically a success for a video
                // the processing timeout already marked Failed.
                logger.LogWarning(
                    "Ignoring video-processed webhook for video {VideoId} in {Status}, not Processing (success: {Success})",
                    cmd.VideoId, video.Status, cmd.Success);
                return Unit.Value;
            }

            var now = clock.UtcNow;

            // A "success" without the transcode output is not a success — persisting it
            // would throw and leave the video stuck in Processing forever.
            var hasProcessedVideo = !string.IsNullOrWhiteSpace(cmd.ProcessedPath);
            if (cmd.Success && hasProcessedVideo)
                PersistSuccessfulProcessing(video, cmd, now);
            else
                video.MarkAsFailed(now);

            await db.SaveChangesAsync(ct);

            return Unit.Value;
        }

        private void PersistSuccessfulProcessing(Video video, Command cmd, DateTimeOffset now)
        {
            // Non-critical steps (thumbnails/audio/preview/streaming) may fail while the
            // job still reports success, so every optional artifact can be absent.
            var artifacts = new VideoArtifacts(
                cmd.VideoId,
                cmd.ProcessedPath!,
                cmd.PreviewPath,
                cmd.HlsPath,
                cmd.AudioPath,
                cmd.ThumbnailPaths,
                now);

            video.MarkAsReady(now);
            db.VideoArtifacts.Add(artifacts);

            // `analyze` is semi-critical: when it fails the webhook carries no metadata at all.
            var hasMetadata = cmd.FileSizeBytes is not null && cmd.DurationSeconds is not null
                && cmd.Width is not null && cmd.Height is not null
                && !string.IsNullOrWhiteSpace(cmd.Codec);
            if (!hasMetadata)
                return;

            db.VideoMetadata.Add(new VideoMetadata(
                cmd.VideoId,
                cmd.FileSizeBytes!.Value,
                cmd.DurationSeconds!.Value,
                cmd.Width!.Value,
                cmd.Height!.Value,
                cmd.Codec!,
                now));
        }
    }
}
