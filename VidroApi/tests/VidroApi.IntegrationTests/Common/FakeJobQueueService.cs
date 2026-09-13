using VidroApi.Application.Abstractions;

namespace VidroApi.IntegrationTests.Common;

public class FakeJobQueueService : IJobQueueService
{
    /// <summary>Every job published in this process, so a test can assert what crossed the queue.</summary>
    public static readonly List<PublishedJob> Published = [];

    public Task PublishJobAsync(string videoId, string callbackUrl, string correlationId, CancellationToken ct = default)
    {
        lock (Published)
            Published.Add(new PublishedJob(videoId, callbackUrl, correlationId));

        return Task.CompletedTask;
    }

    public record PublishedJob(string VideoId, string CallbackUrl, string CorrelationId);
}
