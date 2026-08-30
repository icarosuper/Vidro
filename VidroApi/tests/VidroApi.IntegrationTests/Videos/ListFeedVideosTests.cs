using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using VidroApi.IntegrationTests.Common;

namespace VidroApi.IntegrationTests.Videos;

public class ListFeedVideosTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private const string WebhookSecret = "test-webhook-secret";
    private const string MinioUploadToken = "test-minio-upload-token";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Default)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task ListFeedVideos_WithoutAuthentication_Returns401()
    {
        var response = await _client.GetAsync("/v1/feed?limit=10");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListFeedVideos_WithNoFollowedChannels_ReturnsEmptyList()
    {
        var accessToken = await SignUpAndGetAccessToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.GetAsync("/v1/feed?limit=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var videos = body.GetProperty("data").GetProperty("videos");
        videos.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ListFeedVideos_ReturnsReadyVideosFromFollowedChannels()
    {
        var (creatorToken, creatorUsername, creatorHandle) = await CreateChannelAndGetIds();
        var (followerToken, _, _) = await CreateChannelAndGetIds();

        await FollowChannelAsync(followerToken, creatorUsername, creatorHandle);

        var videoId = await CreateReadyVideoAsync(creatorToken, creatorUsername, creatorHandle, "Feed Video");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", followerToken);
        var response = await _client.GetAsync("/v1/feed?limit=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var videos = body.GetProperty("data").GetProperty("videos");
        videos.GetArrayLength().Should().Be(1);
        videos[0].GetProperty("videoId").GetString().Should().Be(videoId.ToString());
        videos[0].GetProperty("title").GetString().Should().Be("Feed Video");
    }

    [Fact]
    public async Task ListFeedVideos_DoesNotIncludeOwnChannelVideos()
    {
        var (userToken, userUsername, userHandle) = await CreateChannelAndGetIds();
        var (otherToken, otherUsername, otherHandle) = await CreateChannelAndGetIds();

        // User follows their own channel (unusual but possible via API if allowed) and another channel
        await FollowChannelAsync(userToken, otherUsername, otherHandle);
        await CreateReadyVideoAsync(userToken, userUsername, userHandle, "Own Video");
        await CreateReadyVideoAsync(otherToken, otherUsername, otherHandle, "Other Video");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
        var response = await _client.GetAsync("/v1/feed?limit=10");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var videos = body.GetProperty("data").GetProperty("videos");
        var titles = Enumerable.Range(0, videos.GetArrayLength())
            .Select(i => videos[i].GetProperty("title").GetString())
            .ToList();

        titles.Should().Contain("Other Video");
        titles.Should().NotContain("Own Video");
    }

    [Fact]
    public async Task ListFeedVideos_DoesNotIncludeNonReadyVideos()
    {
        var (creatorToken, creatorUsername, creatorHandle) = await CreateChannelAndGetIds();
        var (followerToken, _, _) = await CreateChannelAndGetIds();

        await FollowChannelAsync(followerToken, creatorUsername, creatorHandle);

        // Create a video but do NOT complete processing — stays in PendingUpload
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", creatorToken);
        await _client.PostAsJsonAsync($"/v1/users/{creatorUsername}/channels/{creatorHandle}/videos", new
        {
            title = "Pending Video",
            tags = Array.Empty<string>(),
            visibility = 0
        });

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", followerToken);
        var response = await _client.GetAsync("/v1/feed?limit=10");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var videos = body.GetProperty("data").GetProperty("videos");
        videos.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ListFeedVideos_DoesNotIncludePrivateVideos()
    {
        var (creatorToken, creatorUsername, creatorHandle) = await CreateChannelAndGetIds();
        var (followerToken, _, _) = await CreateChannelAndGetIds();

        await FollowChannelAsync(followerToken, creatorUsername, creatorHandle);
        await CreateReadyVideoAsync(creatorToken, creatorUsername, creatorHandle, "Private Video", visibility: 2);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", followerToken);
        var response = await _client.GetAsync("/v1/feed?limit=10");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var videos = body.GetProperty("data").GetProperty("videos");
        videos.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ListFeedVideos_ReturnsThumbnailUrls()
    {
        var (creatorToken, creatorUsername, creatorHandle) = await CreateChannelAndGetIds();
        var (followerToken, _, _) = await CreateChannelAndGetIds();

        await FollowChannelAsync(followerToken, creatorUsername, creatorHandle);
        await CreateReadyVideoAsync(creatorToken, creatorUsername, creatorHandle, "Video With Thumbnail");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", followerToken);
        var response = await _client.GetAsync("/v1/feed?limit=10");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var video = body.GetProperty("data").GetProperty("videos")[0];
        var thumbnailUrls = video.GetProperty("thumbnailUrls");
        thumbnailUrls.GetArrayLength().Should().BeGreaterThan(0);
        thumbnailUrls[0].GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ListFeedVideos_PaginationWithCursor_ReturnsNextPage()
    {
        var (creatorToken, creatorUsername, creatorHandle) = await CreateChannelAndGetIds();
        var (followerToken, _, _) = await CreateChannelAndGetIds();

        await FollowChannelAsync(followerToken, creatorUsername, creatorHandle);

        for (var i = 0; i < 3; i++)
            await CreateReadyVideoAsync(creatorToken, creatorUsername, creatorHandle, $"Video {i}");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", followerToken);
        var firstPage = await _client.GetAsync("/v1/feed?limit=2");
        var firstBody = await firstPage.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var firstData = firstBody.GetProperty("data");
        firstData.GetProperty("videos").GetArrayLength().Should().Be(2);

        var nextCursor = firstData.GetProperty("nextCursor").GetString();
        nextCursor.Should().NotBeNull();

        var secondPage = await _client.GetAsync($"/v1/feed?limit=2&cursor={Uri.EscapeDataString(nextCursor)}");
        var secondBody = await secondPage.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var secondData = secondBody.GetProperty("data");
        secondData.GetProperty("videos").GetArrayLength().Should().Be(1);
        secondData.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private async Task FollowChannelAsync(string accessToken, string username, string handle)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        await _client.PostAsync($"/v1/users/{username}/channels/{handle}/follow", null);
    }

    private async Task<Guid> CreateReadyVideoAsync(string accessToken, string username, string channelHandle, string title, int visibility = 0)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var createResponse = await _client.PostAsJsonAsync($"/v1/users/{username}/channels/{channelHandle}/videos", new
        {
            title,
            tags = Array.Empty<string>(),
            visibility
        });
        var createBody = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var videoId = Guid.Parse(createBody.GetProperty("data").GetProperty("videoId").GetString()!);

        await SimulateProcessingCompletedAsync(videoId);

        return videoId;
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

    private async Task<(string AccessToken, string Username, string Handle)> CreateChannelAndGetIds()
    {
        var username = $"usr{Guid.NewGuid():N}"[..15];
        var email = $"user_{Guid.NewGuid():N}@example.com";
        var password = "StrongPass1!";

        await _client.PostAsJsonAsync("/v1/auth/signup", new { username, email, password });

        var signInResponse = await _client.PostAsJsonAsync("/v1/auth/signin", new { email, password });
        var signInBody = await signInResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var accessToken = signInBody.GetProperty("data").GetProperty("accessToken").GetString()!;

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var handle = $"ch{Guid.NewGuid():N}"[..15];
        await _client.PostAsJsonAsync("/v1/channels", new { handle, name = "My Channel" });

        return (accessToken, username, handle);
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
