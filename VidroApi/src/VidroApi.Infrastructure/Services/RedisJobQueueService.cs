using System.Text.Json;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using VidroApi.Application.Abstractions;
using VidroApi.Infrastructure.Settings;

namespace VidroApi.Infrastructure.Services;

public class RedisJobQueueService(IConnectionMultiplexer redis, IOptions<JobQueueSettings> options) : IJobQueueService
{
    private readonly string _queueName = options.Value.QueueName;

    public async Task PublishJobAsync(string videoId, string callbackUrl, string correlationId, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();

        await db.StringSetAsync(
            JobStateKey(videoId),
            SerializeJobState(callbackUrl, correlationId, DateTimeOffset.UtcNow),
            TimeSpan.FromHours(24));

        await db.ListLeftPushAsync(_queueName, videoId);
    }

    /// <summary>The Redis key the worker reads the job envelope from.</summary>
    public static string JobStateKey(string videoId) => $"job:{videoId}";

    /// <summary>
    /// The job envelope as the worker deserializes it — snake_case because it is a shared contract,
    /// locked by <c>contracts/job-state.json</c> (see contracts/README.md).
    /// </summary>
    public static string SerializeJobState(string callbackUrl, string correlationId, DateTimeOffset now)
    {
        var nowUnixSeconds = now.ToUnixTimeSeconds();
        var jobState = new
        {
            status = "pending",
            callback_url = callbackUrl,
            correlation_id = correlationId,
            retry_count = 0,
            created_at = nowUnixSeconds,
            updated_at = nowUnixSeconds
        };
        return JsonSerializer.Serialize(jobState);
    }
}
