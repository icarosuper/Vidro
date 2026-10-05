using Microsoft.EntityFrameworkCore;
using VidroApi.Application.Abstractions;
using VidroApi.Domain.Enums;
using VidroApi.Infrastructure.Persistence;

namespace VidroApi.IntegrationTests.Common;

/// <param name="db">The request's own context, so the status read at publish time sees what the
/// handler has already saved — inside its transaction, too — and nothing it has only staged.</param>
public class FakeJobQueueService(AppDbContext db) : IJobQueueService
{
    /// <summary>Every job published in this process, so a test can assert what crossed the queue.</summary>
    public static readonly List<PublishedJob> Published = [];

    /// <summary>Video ids whose publish throws, standing in for Redis being down.</summary>
    public static readonly HashSet<string> FailingVideoIds = [];

    /// <summary>Video ids whose publish is cancelled, standing in for a shutdown mid-publish.</summary>
    public static readonly HashSet<string> CancellingVideoIds = [];

    public async Task PublishJobAsync(string videoId, string callbackUrl, string correlationId, CancellationToken ct = default)
    {
        bool publishFails;
        lock (FailingVideoIds)
            publishFails = FailingVideoIds.Contains(videoId);
        if (publishFails)
            throw new InvalidOperationException($"Simulated Redis failure publishing {videoId}");

        bool publishIsCancelled;
        lock (CancellingVideoIds)
            publishIsCancelled = CancellingVideoIds.Contains(videoId);
        if (publishIsCancelled)
            throw new OperationCanceledException();

        var savedStatus = await db.Videos.AsNoTracking()
            .Where(v => v.Id == Guid.Parse(videoId))
            .Select(v => (VideoStatus?)v.Status)
            .FirstOrDefaultAsync(ct);

        lock (Published)
            Published.Add(new PublishedJob(videoId, callbackUrl, correlationId, savedStatus));
    }

    /// <param name="SavedStatus">The video's status in the database when the job was published.</param>
    public record PublishedJob(string VideoId, string CallbackUrl, string CorrelationId, VideoStatus? SavedStatus);
}
