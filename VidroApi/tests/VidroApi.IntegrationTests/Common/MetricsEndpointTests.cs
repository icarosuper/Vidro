using FluentAssertions;

namespace VidroApi.IntegrationTests.Common;

/// <summary>
/// Prometheus scrapes /metrics. Nothing here is hand-written instrumentation — the assertions
/// prove the runtime's own meters reach the exporter, and that the Npgsql pool label is the
/// fixed data source name instead of the connection string.
/// </summary>
public class MetricsEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Metrics_AfterARequest_ExposesHttpServerMetrics()
    {
        await _client.GetAsync("/health");

        var scrape = await _client.GetStringAsync("/metrics");

        scrape.Should().Contain("http_server_request_duration_seconds");
    }

    [Fact]
    public async Task Metrics_LabelThePoolByName_NotByConnectionString()
    {
        var scrape = await _client.GetStringAsync("/metrics");

        scrape.Should().Contain("db_client_connection_pool_name=\"vidroapi\"");
        scrape.Should().NotContain("Host=");
    }
}
