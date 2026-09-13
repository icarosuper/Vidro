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

        // Field names are snake_case because this object is the shared contract with the worker:
        // VidroProcessor/queue/job.go deserializes exactly these names.
        var jobState = new
        {
            status = "pending",
            callback_url = callbackUrl,
            correlation_id = correlationId,
            retry_count = 0,
            created_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            updated_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        await db.StringSetAsync(
            $"job:{videoId}",
            JsonSerializer.Serialize(jobState),
            TimeSpan.FromHours(24));

        await db.ListLeftPushAsync(_queueName, videoId);
    }
}
