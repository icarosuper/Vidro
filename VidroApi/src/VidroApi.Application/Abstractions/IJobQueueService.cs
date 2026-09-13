namespace VidroApi.Application.Abstractions;

public interface IJobQueueService
{
    /// <summary>
    /// Publishes a processing job. <paramref name="correlationId"/> travels in the job envelope —
    /// a queue has no headers, so the producer has to put it in the payload — and the worker logs
    /// the job under it and sends it back on the completion webhook.
    /// </summary>
    Task PublishJobAsync(string videoId, string callbackUrl, string correlationId, CancellationToken ct = default);
}
