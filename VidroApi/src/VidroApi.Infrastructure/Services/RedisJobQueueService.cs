using System.Text.Json;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using VidroApi.Application.Abstractions;
using VidroApi.Infrastructure.Settings;

namespace VidroApi.Infrastructure.Services;

public class RedisJobQueueService(IConnectionMultiplexer redis, IOptions<JobQueueSettings> options) : IJobQueueService
{
    private readonly string _queueName = options.Value.QueueName;

    public async Task PublishJobAsync(string videoId, string callbackUrl, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();

        var jobState = new
        {
            status = "pending",
            callback_url = callbackUrl,
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
