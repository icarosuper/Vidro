using System.Text.Json;
using FluentAssertions;

namespace VidroApi.UnitTests.Contracts;

/// <summary>
/// Reconciliation marks a video Failed once it sits in Processing longer than
/// <c>VideoSettings:ProcessingTimeoutMinutes</c>, and a success webhook that arrives after that is
/// dropped. The timeout therefore has to outlast the worker's worst case with every retry spent.
/// <c>contracts/processing-timeout.json</c> holds that worst case:
/// <c>VidroProcessor/processing_timeout_contract_test.go</c> proves it matches the worker, this
/// proves the API's configured timeout stays above it.
/// </summary>
public class ProcessingTimeoutContractTests
{
    [Fact]
    public void ProcessingTimeout_ShouldOutlastTheWorkersWorstCase()
    {
        var goldenPath = Path.Combine(AppContext.BaseDirectory, "contracts", "processing-timeout.json");
        using var golden = JsonDocument.Parse(File.ReadAllText(goldenPath));
        var workerWorstCaseMinutes = golden.RootElement.GetProperty("workerWorstCaseMinutes").GetInt32();

        var appSettingsPath = Path.Combine(AppContext.BaseDirectory, "api-appsettings.json");
        using var appSettings = JsonDocument.Parse(File.ReadAllText(appSettingsPath));
        var processingTimeoutMinutes = appSettings.RootElement
            .GetProperty("VideoSettings").GetProperty("ProcessingTimeoutMinutes").GetInt32();

        processingTimeoutMinutes.Should().BeGreaterThan(workerWorstCaseMinutes,
            "a job the worker is still retrying must not be marked Failed by reconciliation");
    }
}
