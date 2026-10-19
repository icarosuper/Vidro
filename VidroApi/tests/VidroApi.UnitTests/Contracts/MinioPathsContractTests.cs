using System.Text.Json.Nodes;
using FluentAssertions;
using VidroApi.Infrastructure.Services;

namespace VidroApi.UnitTests.Contracts;

/// <summary>
/// The MinIO object layout is a contract with the worker: the API writes <c>raw/&lt;id&gt;</c>
/// (and a custom thumbnail under the same prefix the worker uses for generated ones), parses the
/// MinIO notification key, and cleans up by those prefixes. <c>contracts/minio-paths.json</c> is
/// the contract. This proves the API builds exactly those keys;
/// <c>VidroProcessor/minio_paths_contract_test.go</c> proves the worker reads and writes the same
/// file. See contracts/README.md.
/// </summary>
public class MinioPathsContractTests
{
    private static readonly Guid PlaceholderVideoId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly JsonNode Golden = JsonNode.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "contracts", "minio-paths.json")))!;

    [Fact]
    public void ObjectKeys_ShouldMatchTheGolden()
    {
        MinioObjectKeys.RawPrefix.Should().Be(GoldenString("rawPrefix"),
            "the MinIO notification and the worker download both look under this prefix");
        MinioObjectKeys.Raw(PlaceholderVideoId).Should().Be(GoldenString("raw"),
            "the browser PUTs here and the worker downloads this object");
        MinioObjectKeys.RawArchived(PlaceholderVideoId).Should().Be(GoldenString("rawArchived"));
        MinioObjectKeys.Processed(PlaceholderVideoId).Should().Be(GoldenString("processed"));
        MinioObjectKeys.Audio(PlaceholderVideoId).Should().Be(GoldenString("audio"));
        MinioObjectKeys.Preview(PlaceholderVideoId).Should().Be(GoldenString("preview"));
        MinioObjectKeys.ThumbnailsPrefix(PlaceholderVideoId).Should().Be(GoldenString("thumbnailsPrefix"));
        MinioObjectKeys.HlsPrefix(PlaceholderVideoId).Should().Be(GoldenString("hlsPrefix"));
        MinioObjectKeys.CustomThumbnail(PlaceholderVideoId).Should().Be(GoldenString("customThumbnail"),
            "the custom thumbnail lives under the same prefix the worker writes generated ones to");
    }

    [Fact]
    public void ThumbnailPaths_ShouldMatchTheGolden()
    {
        var thumbnailCount = Golden["thumbnailCount"]!.GetValue<int>();
        MinioObjectKeys.ThumbnailCount.Should().Be(thumbnailCount);

        var goldenPaths = Golden["thumbnailPaths"]!.AsArray()
            .Select(node => node!.GetValue<string>())
            .ToArray();

        MinioObjectKeys.ThumbnailPaths(PlaceholderVideoId).Should().Equal(goldenPaths,
            "the webhook lists these files; a different pattern is a 404 on every card");
    }

    [Fact]
    public void TryParseVideoIdFromNotificationKey_ShouldReadTheGoldenRawKey()
    {
        var notificationKey = $"videos/{GoldenString("raw")}";

        var videoId = MinioObjectKeys.TryParseVideoIdFromNotificationKey(notificationKey);

        videoId.Should().Be(PlaceholderVideoId,
            "MinIO notifies as {{bucket}}/raw/{{videoId}}; a different raw segment drops every upload");
    }

    [Fact]
    public void TryParseVideoIdFromNotificationKey_ShouldIgnoreAKeyOutsideRaw()
    {
        var processedKey = $"videos/{GoldenString("processed")}";

        var videoId = MinioObjectKeys.TryParseVideoIdFromNotificationKey(processedKey);

        videoId.Should().BeNull("only a put under raw/ is an upload completing");
    }

    private static string GoldenString(string property) => Golden[property]!.GetValue<string>();
}
