using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSharpFunctionalExtensions;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VidroApi.Application.Abstractions;
using VidroApi.Domain.Entities;
using VidroApi.Domain.Enums;
using VidroApi.Domain.Errors;
using VidroApi.Domain.Errors.EntityErrors;
using VidroApi.Infrastructure.Persistence;
using VidroApi.Infrastructure.Settings;

namespace VidroApi.Api.Features.Videos;

public static class VideoProcessed
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    public record Command : IRequest<UnitResult<Error>>
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

            var cmd = JsonSerializer.Deserialize<Command>(rawBody, JsonOptions);
            if (cmd is null)
                return Results.BadRequest();

            await mediator.Send(cmd, ct);
            return Results.Ok();
        });

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

    public class Handler(AppDbContext db, IDateTimeProvider clock)
        : IRequestHandler<Command, UnitResult<Error>>
    {
        public async ValueTask<UnitResult<Error>> Handle(Command cmd, CancellationToken ct)
        {
            var video = await db.Videos.FirstOrDefaultAsync(v => v.Id == cmd.VideoId, ct);
            if (video is null)
                return CommonErrors.NotFound(nameof(Video), cmd.VideoId);

            var isProcessing = video.Status == VideoStatus.Processing;
            if (!isProcessing)
                return Errors.Video.NotInProcessingState();

            var now = clock.UtcNow;

            // A "success" without the transcode output is not a success — persisting it
            // would throw and leave the video stuck in Processing forever.
            var hasProcessedVideo = !string.IsNullOrWhiteSpace(cmd.ProcessedPath);
            if (cmd.Success && hasProcessedVideo)
                PersistSuccessfulProcessing(video, cmd, now);
            else
                video.MarkAsFailed(now);

            await db.SaveChangesAsync(ct);

            return UnitResult.Success<Error>();
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
