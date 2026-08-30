using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using VidroApi.IntegrationTests.Common;

namespace VidroApi.IntegrationTests.Videos;

public class WebhookTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private const string WebhookSecret = "test-webhook-secret";
    private const string MinioUploadToken = "test-minio-upload-token";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Default)
    {
        PropertyNameCaseInsensitive = true
    };

    // --- MinioUploadCompleted ---

    [Fact]
    public async Task MinioUploadCompleted_WithValidToken_Returns200()
    {
        var videoId = await CreatePendingVideo();

        var response = await SendMinioWebhook(videoId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MinioUploadCompleted_MovesVideoToProcessing()
    {
        var (accessToken, videoId) = await CreateChannelAndPendingVideo();
        await SendMinioWebhook(videoId);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var getResponse = await _client.GetAsync($"/v1/videos/{videoId}");

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await getResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("data").GetProperty("status").GetProperty("value").GetString().Should().Be("Processing");
    }

    [Fact]
    public async Task MinioUploadCompleted_WithInvalidToken_Returns401()
    {
        var payload = JsonSerializer.Serialize(new
        {
            EventName = "s3:ObjectCreated:Put",
            Key = $"test-bucket/raw/{Guid.NewGuid()}"
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/minio-upload-completed")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-token");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MinioUploadCompleted_DuplicateWebhook_Returns200()
    {
        var videoId = await CreatePendingVideo();

        await SendMinioWebhook(videoId);
        var response = await SendMinioWebhook(videoId); // second call, video already Processing

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // --- VideoProcessed ---

    [Fact]
    public async Task VideoProcessed_WithValidSignature_Returns200()
    {
        var videoId = await CreatePendingVideo();
        await SendMinioWebhook(videoId);

        var response = await SendVideoProcessedWebhook(videoId, success: true);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task VideoProcessed_SuccessfulProcessing_VideoBecomesReady()
    {
        var videoId = await CreatePendingVideo();
        await SendMinioWebhook(videoId);
        await SendVideoProcessedWebhook(videoId, success: true);

        var getResponse = await _client.GetAsync($"/v1/videos/{videoId}");

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await getResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("data").GetProperty("status").GetProperty("value").GetString().Should().Be("Ready");
    }

    [Fact]
    public async Task VideoProcessed_FailedProcessing_VideoBecomesFailed()
    {
        var (accessToken, videoId) = await CreateChannelAndPendingVideo();
        await SendMinioWebhook(videoId);
        await SendVideoProcessedWebhook(videoId, success: false);

        // Owner can still see the video
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var getResponse = await _client.GetAsync($"/v1/videos/{videoId}");

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await getResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("data").GetProperty("status").GetProperty("value").GetString().Should().Be("Failed");
    }

    [Fact]
    public async Task VideoProcessed_WithInvalidSignature_Returns401()
    {
        var payload = JsonSerializer.Serialize(new
        {
            videoId = Guid.NewGuid(),
            success = true,
            processedPath = "processed/x",
            previewPath = "preview/x",
            hlsPath = "hls/x/",
            audioPath = "audio/x.mp3",
            thumbnailPaths = Array.Empty<string>(),
            fileSizeBytes = 1000L,
            durationSeconds = 60.0,
            width = 1920,
            height = 1080,
            codec = "h264"
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/video-processed")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Webhook-Signature", "sha256=invalidsignature");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<Guid> CreatePendingVideo()
    {
        var (_, videoId) = await CreateChannelAndPendingVideo();
        return videoId;
    }

    private async Task<(string AccessToken, Guid VideoId)> CreateChannelAndPendingVideo()
    {
        var (accessToken, username, channelHandle) = await CreateChannelAndGetIds();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var createResponse = await _client.PostAsJsonAsync($"/v1/users/{username}/channels/{channelHandle}/videos", new
        {
            title = "Test Video",
            tags = Array.Empty<string>(),
            visibility = 0
        });
        var createBody = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var videoId = Guid.Parse(createBody.GetProperty("data").GetProperty("videoId").GetString()!);

        return (accessToken, videoId);
    }

    private async Task<HttpResponseMessage> SendMinioWebhook(Guid videoId)
    {
        var payload = JsonSerializer.Serialize(new
        {
            EventName = "s3:ObjectCreated:Put",
            Key = $"test-bucket/raw/{videoId}"
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/minio-upload-completed")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MinioUploadToken);

        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendVideoProcessedWebhook(Guid videoId, bool success)
    {
        var payload = JsonSerializer.Serialize(new
        {
            videoId,
            success,
            processedPath = success ? $"processed/{videoId}_processed" : null,
            previewPath = success ? $"preview/{videoId}_preview.mp4" : null,
            hlsPath = success ? $"hls/{videoId}/" : null,
            audioPath = success ? $"audio/{videoId}.mp3" : null,
            thumbnailPaths = success ? new[] { $"thumbnails/{videoId}/thumb1.jpg" } : null,
            fileSizeBytes = success ? (long?)10_000_000L : null,
            durationSeconds = success ? (double?)120.5 : null,
            width = success ? (int?)1920 : null,
            height = success ? (int?)1080 : null,
            codec = success ? "h264" : null
        });

        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var signature = ComputeHmacSignature(payloadBytes, WebhookSecret);

        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/video-processed")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Webhook-Signature", $"sha256={signature}");

        return await _client.SendAsync(request);
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
