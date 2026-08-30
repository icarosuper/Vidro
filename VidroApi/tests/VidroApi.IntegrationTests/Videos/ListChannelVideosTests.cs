using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using VidroApi.IntegrationTests.Common;

namespace VidroApi.IntegrationTests.Videos;

public class ListChannelVideosTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private const string WebhookSecret = "test-webhook-secret";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Default)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task ListChannelVideos_WithPublicVideos_Returns200WithList()
    {
        var (_, username, channelHandle) = await CreateChannelAndGetIds();

        await CreateAndReadyVideo(username, channelHandle, visibility: 0);

        var response = await _client.GetAsync($"/v1/users/{username}/channels/{channelHandle}/videos?limit=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var videos = body.GetProperty("data").GetProperty("videos");
        videos.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ListChannelVideos_PrivateVideoHiddenFromNonOwner()
    {
        var (_, username, channelHandle) = await CreateChannelAndGetIds();

        await CreateAndReadyVideo(username, channelHandle, visibility: 2); // Private

        _client.DefaultRequestHeaders.Authorization = null;
        var response = await _client.GetAsync($"/v1/users/{username}/channels/{channelHandle}/videos?limit=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var videos = body.GetProperty("data").GetProperty("videos");
        videos.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ListChannelVideos_PrivateVideoVisibleToOwner()
    {
        var (accessToken, username, channelHandle) = await CreateChannelAndGetIds();

        await CreateAndReadyVideo(username, channelHandle, visibility: 2); // Private

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await _client.GetAsync($"/v1/users/{username}/channels/{channelHandle}/videos?limit=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var videos = body.GetProperty("data").GetProperty("videos");
        videos.GetArrayLength().Should().Be(1);
        videos[0].GetProperty("status").GetProperty("value").GetString().Should().Be("Ready");
    }

    [Fact]
    public async Task ListChannelVideos_PendingVideoVisibleToOwnerWithStatus()
    {
        var (accessToken, username, channelHandle) = await CreateChannelAndGetIds();

        // Create video but do NOT trigger processing — stays PendingUpload
        var createResponse = await _client.PostAsJsonAsync($"/v1/users/{username}/channels/{channelHandle}/videos", new
        {
            title = "Pending Video",
            tags = Array.Empty<string>(),
            visibility = 0
        });
        var createBody = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        _ = Guid.Parse(createBody.GetProperty("data").GetProperty("videoId").GetString()!);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var response = await _client.GetAsync($"/v1/users/{username}/channels/{channelHandle}/videos?limit=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var videos = body.GetProperty("data").GetProperty("videos");
        videos.GetArrayLength().Should().Be(1);
        videos[0].GetProperty("status").GetProperty("value").GetString().Should().Be("PendingUpload");
    }

    [Fact]
    public async Task ListChannelVideos_NonExistentChannel_Returns404()
    {
        var response = await _client.GetAsync("/v1/users/nonexistent/channels/nonexistent/videos?limit=10");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListChannelVideos_PaginationReturnsCursorWhenMoreItems()
    {
        var (_, username, channelHandle) = await CreateChannelAndGetIds();

        for (var i = 0; i < 3; i++)
            await CreateAndReadyVideo(username, channelHandle, visibility: 0);

        var response = await _client.GetAsync($"/v1/users/{username}/channels/{channelHandle}/videos?limit=2");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        data.GetProperty("videos").GetArrayLength().Should().Be(2);
        data.GetProperty("nextCursor").ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    private async Task CreateAndReadyVideo(string username, string channelHandle, int visibility)
    {
        var createResponse = await _client.PostAsJsonAsync($"/v1/users/{username}/channels/{channelHandle}/videos", new
        {
            title = "Test Video",
            tags = Array.Empty<string>(),
            visibility
        });
        var createBody = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var videoId = Guid.Parse(createBody.GetProperty("data").GetProperty("videoId").GetString()!);

        await SimulateProcessingCompletedAsync(videoId);
    }

    private async Task SimulateProcessingCompletedAsync(Guid videoId)
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
        minioRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-minio-upload-token");
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

    private async Task<(string AccessToken, string Username, string ChannelHandle)> CreateChannelAndGetIds()
    {
        var username = $"usr{Guid.NewGuid():N}"[..15];
        var email = $"user_{Guid.NewGuid():N}@example.com";
        var password = "StrongPass1!";

        await _client.PostAsJsonAsync("/v1/auth/signup", new { username, email, password });

        var signInResponse = await _client.PostAsJsonAsync("/v1/auth/signin", new { email, password });
        var signInBody = await signInResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var accessToken = signInBody.GetProperty("data").GetProperty("accessToken").GetString()!;

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        await _client.PostAsJsonAsync("/v1/channels", new { handle = "test-channel", name = "My Channel" });

        return (accessToken, username, "test-channel");
    }

    private async Task<string> SignUpAndGetAccessToken()
    {
        var email = $"user_{Guid.NewGuid():N}@example.com";
        var password = "StrongPass1!";

        await _client.PostAsJsonAsync("/v1/auth/signup", new
        {
            username = $"usr{Guid.NewGuid():N}"[..15],
            email,
            password
        });

        var signInResponse = await _client.PostAsJsonAsync("/v1/auth/signin", new { email, password });
        var body = await signInResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return body.GetProperty("data").GetProperty("accessToken").GetString()!;
    }
}
