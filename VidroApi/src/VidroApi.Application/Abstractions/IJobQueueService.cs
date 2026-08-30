namespace VidroApi.Application.Abstractions;

public interface IJobQueueService
{
    Task PublishJobAsync(string videoId, string callbackUrl, CancellationToken ct = default);
}
