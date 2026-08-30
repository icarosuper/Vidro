using VidroApi.Application.Abstractions;

namespace VidroApi.IntegrationTests.Common;

public class FakeJobQueueService : IJobQueueService
{
    public Task PublishJobAsync(string videoId, string callbackUrl, CancellationToken ct = default) =>
        Task.CompletedTask;
}
