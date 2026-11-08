using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using VidroApi.IntegrationTests.Common;

namespace VidroApi.IntegrationTests.Playlists;

public class ListPlaylistsContainsVideoTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private const string WebhookSecret = "test-webhook-secret";
    private const string MinioUploadToken = "test-minio-upload-token";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Default)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task ListUserPlaylists_WithVideoId_MarksThePlaylistsThatHoldIt()
    {
        var (token, _, username, channelHandle) = await CreateChannelAndGetIds();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var videoId = await CreateReadyVideo(username, channelHandle);
        var withVideo = await CreatePlaylist(new { name = "With", visibility = 0, scope = 0 });
        var withoutVideo = await CreatePlaylist(new { name = "Without", visibility = 0, scope = 0 });
        await _client.PostAsJsonAsync($"/v1/playlists/{withVideo}/items", new { videoId });

        var items = await ListItems($"/v1/users/{username}/playlists?limit=20&videoId={videoId}");

        ContainsVideoOf(items, withVideo).Should().BeTrue();
        ContainsVideoOf(items, withoutVideo).Should().BeFalse();
    }

    [Fact]
    public async Task ListUserPlaylists_WithoutVideoId_LeavesContainsVideoNull()
    {
        var (token, _, username, _) = await CreateChannelAndGetIds();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var playlistId = await CreatePlaylist(new { name = "Any", visibility = 0, scope = 0 });

        var items = await ListItems($"/v1/users/{username}/playlists?limit=20");

        var item = items.Single(i => i.GetProperty("id").GetString() == playlistId.ToString());
        item.GetProperty("containsVideo").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task ListChannelPlaylists_WithVideoId_MarksThePlaylistsThatHoldIt()
    {
        var (token, channelId, username, channelHandle) = await CreateChannelAndGetIds();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var videoId = await CreateReadyVideo(username, channelHandle);
        var withVideo = await CreatePlaylist(new { name = "With", visibility = 0, scope = 1, channelId });
        var withoutVideo = await CreatePlaylist(new { name = "Without", visibility = 0, scope = 1, channelId });
        await _client.PostAsJsonAsync($"/v1/playlists/{withVideo}/items", new { videoId });

        var items = await ListItems(
            $"/v1/users/{username}/channels/{channelHandle}/playlists?limit=20&videoId={videoId}");

        ContainsVideoOf(items, withVideo).Should().BeTrue();
        ContainsVideoOf(items, withoutVideo).Should().BeFalse();
    }

    private static bool ContainsVideoOf(List<JsonElement> items, Guid playlistId) =>
        items.Single(i => i.GetProperty("id").GetString() == playlistId.ToString())
            .GetProperty("containsVideo").GetBoolean();

    private async Task<List<JsonElement>> ListItems(string url)
    {
        var response = await _client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return body.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
    }

    private async Task<Guid> CreatePlaylist(object request)
    {
        var response = await _client.PostAsJsonAsync("/v1/playlists", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return Guid.Parse(body.GetProperty("data").GetProperty("playlistId").GetString()!);
    }

    private async Task<Guid> CreateReadyVideo(string username, string channelHandle)
    {
        var createResponse = await _client.PostAsJsonAsync($"/v1/users/{username}/channels/{channelHandle}/videos", new
        {
            title = "Test Video",
            tags = Array.Empty<string>(),
            visibility = 0
        });
        var createBody = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var videoId = Guid.Parse(createBody.GetProperty("data").GetProperty("videoId").GetString()!);

        await SimulateProcessingCompleted(videoId);

        return videoId;
    }

    private async Task SimulateProcessingCompleted(Guid videoId)
    {
        var minioPayload = JsonSerializer.Serialize(new
        {
            EventName = "s3:ObjectCreated:Put",
            Key = $"test-bucket/raw/{videoId}"
        });
        var minioRequest = new HttpRequestMessage(HttpMethod.Post, "/webhooks/minio-upload-completed")
        {
            Content = new StringContent(minioPayload, Encoding.UTF8, "application/json")
        };
        minioRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MinioUploadToken);
        await _client.SendAsync(minioRequest);

        var processedPayload = JsonSerializer.Serialize(new
        {
            videoId,
            success = true,
            processedPath = $"processed/{videoId}_processed",
            previewPath = $"preview/{videoId}_preview.mp4",
            hlsPath = $"hls/{videoId}/",
            audioPath = $"audio/{videoId}.mp3",
            thumbnailPaths = new[] { $"thumbnails/{videoId}/thumb1.jpg" },
            fileSizeBytes = 10_000_000L,
            durationSeconds = 120.5,
            width = 1920,
            height = 1080,
            codec = "h264"
        });
        var payloadBytes = Encoding.UTF8.GetBytes(processedPayload);
        var signature = ComputeHmacSignature(payloadBytes, WebhookSecret);

        var processedRequest = new HttpRequestMessage(HttpMethod.Post, "/webhooks/video-processed")
        {
            Content = new StringContent(processedPayload, Encoding.UTF8, "application/json")
        };
        processedRequest.Headers.Add("X-Webhook-Signature", $"sha256={signature}");
        await _client.SendAsync(processedRequest);
    }

    private static string ComputeHmacSignature(byte[] payload, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var hash = HMACSHA256.HashData(keyBytes, payload);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<(string Token, Guid ChannelId, string Username, string ChannelHandle)> CreateChannelAndGetIds()
    {
        var username = $"usr{Guid.NewGuid():N}"[..15];
        var email = $"user_{Guid.NewGuid():N}@example.com";
        var password = "StrongPass1!";

        await _client.PostAsJsonAsync("/v1/auth/signup", new { username, email, password });

        var signInResponse = await _client.PostAsJsonAsync("/v1/auth/signin", new { email, password });
        var signInBody = await signInResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var token = signInBody.GetProperty("data").GetProperty("accessToken").GetString()!;

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var channelResponse = await _client.PostAsJsonAsync("/v1/channels", new { handle = "test-channel", name = "My Channel" });
        var channelBody = await channelResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var channelId = Guid.Parse(channelBody.GetProperty("data").GetProperty("channelId").GetString()!);

        return (token, channelId, username, "test-channel");
    }
}
