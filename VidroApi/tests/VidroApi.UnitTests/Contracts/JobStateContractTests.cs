using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using VidroApi.Infrastructure.Services;

namespace VidroApi.UnitTests.Contracts;

/// <summary>
/// The job the API publishes is the worker's only input: a video id on the queue plus the
/// envelope under <c>job:&lt;id&gt;</c>, which carries the <c>callback_url</c> and the
/// <c>correlation_id</c>. <c>contracts/job-state.json</c> is the contract. This proves the API
/// publishes exactly it; <c>VidroProcessor/job_state_contract_test.go</c> proves the worker reads
/// exactly the same file. See contracts/README.md.
/// </summary>
public class JobStateContractTests
{
    private const string PlaceholderVideoId = "11111111-1111-1111-1111-111111111111";

    private static readonly JsonNode Golden = JsonNode.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "contracts", "job-state.json")))!;

    [Fact]
    public void SerializeJobState_ShouldMatchTheGolden()
    {
        var goldenState = Golden["state"]!;
        var callbackUrl = goldenState["callback_url"]!.GetValue<string>();
        var correlationId = goldenState["correlation_id"]!.GetValue<string>();
        var publishedAt = DateTimeOffset.FromUnixTimeSeconds(goldenState["created_at"]!.GetValue<long>());

        var serialized = RedisJobQueueService.SerializeJobState(callbackUrl, correlationId, publishedAt);

        var matchesGolden = JsonNode.DeepEquals(JsonNode.Parse(serialized), goldenState);
        matchesGolden.Should().BeTrue(
            "contracts/job-state.json is what the worker deserializes\n--- expected ---\n{0}\n--- got ---\n{1}",
            goldenState.ToJsonString(), serialized);
    }

    [Fact]
    public void JobStateKey_ShouldMatchTheGolden()
    {
        var goldenStateKey = Golden["stateKey"]!.GetValue<string>();

        RedisJobQueueService.JobStateKey(PlaceholderVideoId).Should().Be(goldenStateKey,
            "the worker looks the envelope up under this key; a different one dead-letters every job");
    }

    [Fact]
    public void QueueName_ShouldMatchTheGolden()
    {
        var goldenQueue = Golden["queue"]!.GetValue<string>();

        var appSettingsPath = Path.Combine(AppContext.BaseDirectory, "api-appsettings.json");
        using var appSettings = JsonDocument.Parse(File.ReadAllText(appSettingsPath));
        var queueName = appSettings.RootElement
            .GetProperty("JobQueueSettings").GetProperty("QueueName").GetString();

        queueName.Should().Be(goldenQueue,
            "the worker consumes PROCESSING_REQUEST_QUEUE; a different name leaves every job unconsumed");
    }
}
