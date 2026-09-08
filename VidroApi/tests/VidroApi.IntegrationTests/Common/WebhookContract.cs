namespace VidroApi.IntegrationTests.Common;

/// <summary>
/// Loads the webhook contract goldens shared with VidroProcessor (contracts/video-processed-*.json).
/// The worker proves it serializes exactly these files
/// (<c>VidroProcessor/webhook_contract_test.go</c>); these tests prove the API accepts exactly
/// them. A field renamed on either side turns both sides red in the same CI run.
/// </summary>
public static class WebhookContract
{
    public const string VideoProcessedSuccessFull = "video-processed-success-full.json";
    public const string VideoProcessedSuccessMinimal = "video-processed-success-minimal.json";
    public const string VideoProcessedSuccessWithoutProcessedPath = "video-processed-success-without-processed-path.json";
    public const string VideoProcessedFailure = "video-processed-failure.json";

    private const string PlaceholderVideoId = "11111111-1111-1111-1111-111111111111";

    /// <summary>
    /// Returns the golden body with the placeholder video id replaced by <paramref name="videoId"/>.
    /// That id is the only thing a test may change — everything else is the contract.
    /// </summary>
    public static string Payload(string goldenFileName, Guid videoId)
    {
        var goldenPath = Path.Combine(AppContext.BaseDirectory, "contracts", goldenFileName);
        var golden = File.ReadAllText(goldenPath);
        return golden.Replace(PlaceholderVideoId, videoId.ToString());
    }
}
