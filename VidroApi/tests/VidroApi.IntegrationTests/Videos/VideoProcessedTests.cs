using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using VidroApi.IntegrationTests.Common;

namespace VidroApi.IntegrationTests.Videos;

public class VideoProcessedTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private const string WebhookSecret = "test-webhook-secret";
    private const string MinioUploadToken = "test-minio-upload-token";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Default)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task VideoProcessed_WithValidSignature_AndSuccess_Returns200()
    {
        var (_, videoId) = await CreateProcessingVideo();

        var response = await SendVideoProcessedWebhookAsync(videoId, success: true, secret: WebhookSecret);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task VideoProcessed_WithValidSignature_AndSuccess_VideoBecomesReady()
    {
        var (_, videoId) = await CreateProcessingVideo();

        await SendVideoProcessedWebhookAsync(videoId, success: true, secret: WebhookSecret);

        var getResponse = await _client.GetAsync($"/v1/videos/{videoId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await getResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        data.GetProperty("status").GetProperty("value").GetString().Should().Be("Ready");
    }

    [Fact]
    public async Task VideoProcessed_WithValidSignature_AndFailure_Returns200()
    {
        var (_, videoId) = await CreateProcessingVideo();

        var response = await SendVideoProcessedWebhookAsync(videoId, success: false, secret: WebhookSecret);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task VideoProcessed_WithValidSignature_AndFailure_VideoBecomesFailedAndIsHiddenFromPublic()
    {
        var (_, videoId) = await CreateProcessingVideo();

        await SendVideoProcessedWebhookAsync(videoId, success: false, secret: WebhookSecret);

        // Failed videos are not Ready, so non-owners cannot see them
        _client.DefaultRequestHeaders.Authorization = null;
        var getResponse = await _client.GetAsync($"/v1/videos/{videoId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task VideoProcessed_WithInvalidSignature_Returns401()
    {
        var (_, videoId) = await CreateProcessingVideo();

        var response = await SendVideoProcessedWebhookAsync(videoId, success: true, secret: "wrong-secret");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task VideoProcessed_WithMissingSignatureHeader_Returns401()
    {
        var (_, videoId) = await CreateProcessingVideo();

        var payload = BuildSuccessPayload(videoId);
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/video-processed")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task VideoProcessed_WithNonExistentVideoId_Returns200AndIsIgnored()
    {
        var response = await SendVideoProcessedWebhookAsync(Guid.NewGuid(), success: true, secret: WebhookSecret);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task VideoProcessed_WhenVideoIsNotInProcessingStatus_Returns200AndIsIgnored()
    {
        var (_, videoId) = await CreateVideoAndGetIds();
        // Video is still in PendingUpload — skip MinioUploadCompleted step

        var response = await SendVideoProcessedWebhookAsync(videoId, success: true, secret: WebhookSecret);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // BUG-1: non-critical steps (thumbnails/audio/preview/streaming) can fail while the
    // Processor still reports success, and `analyze` failing drops metadata entirely.
    // None of that may 500 the handler — a 500 leaves the video in Processing forever.
    [Fact]
    public async Task VideoProcessed_WithSuccess_ButNoOptionalArtifactsOrMetadata_VideoBecomesReady()
    {
        var (_, videoId) = await CreateProcessingVideo();

        var payload = JsonSerializer.Serialize(new
        {
            videoId,
            success = true,
            processedPath = $"processed/{videoId}_processed"
        });

        var response = await SendRawWebhookAsync(payload, WebhookSecret);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await _client.GetAsync($"/v1/videos/{videoId}");
        var body = await getResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        data.GetProperty("status").GetProperty("value").GetString().Should().Be("Ready");
        data.GetProperty("thumbnailUrls").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task VideoProcessed_WithSuccess_ButNoProcessedPath_VideoBecomesFailed()
    {
        var (_, videoId) = await CreateProcessingVideo();

        var payload = JsonSerializer.Serialize(new { videoId, success = true });

        var response = await SendRawWebhookAsync(payload, WebhookSecret);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await _client.GetAsync($"/v1/videos/{videoId}");
        var body = await getResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("data").GetProperty("status").GetProperty("value").GetString().Should().Be("Failed");
    }

    private async Task<HttpResponseMessage> SendRawWebhookAsync(string payload, string secret)
    {
        var signature = ComputeHmacSignature(Encoding.UTF8.GetBytes(payload), secret);
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/video-processed")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Webhook-Signature", $"sha256={signature}");
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendVideoProcessedWebhookAsync(Guid videoId, bool success, string secret)
    {
        var payload = success ? BuildSuccessPayload(videoId) : BuildFailurePayload(videoId);
        return await SendRawWebhookAsync(payload, secret);
    }

    private static string BuildSuccessPayload(Guid videoId) =>
        JsonSerializer.Serialize(new
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

    private static string BuildFailurePayload(Guid videoId) =>
        JsonSerializer.Serialize(new
        {
            videoId,
            success = false
        });

    private static string ComputeHmacSignature(byte[] payload, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var hash = HMACSHA256.HashData(keyBytes, payload);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<(string AccessToken, Guid VideoId)> CreateProcessingVideo()
    {
        var (accessToken, videoId) = await CreateVideoAndGetIds();

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

        return (accessToken, videoId);
    }

    private async Task<(string AccessToken, Guid VideoId)> CreateVideoAndGetIds()
    {
        var (accessToken, username, channelHandle) = await CreateChannelAndGetIds();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var createResponse = await _client.PostAsJsonAsync($"/v1/users/{username}/channels/{channelHandle}/videos", new
        {
            title = "Processing Test Video",
            tags = Array.Empty<string>(),
            visibility = 0
        });
        var createBody = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var videoId = Guid.Parse(createBody.GetProperty("data").GetProperty("videoId").GetString()!);

        return (accessToken, videoId);
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
