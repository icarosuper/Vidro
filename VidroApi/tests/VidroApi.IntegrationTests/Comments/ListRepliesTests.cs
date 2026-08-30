using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using VidroApi.IntegrationTests.Common;

namespace VidroApi.IntegrationTests.Comments;

public class ListRepliesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private const string WebhookSecret = "test-webhook-secret";
    private const string MinioUploadToken = "test-minio-upload-token";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Default)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task ListReplies_CommentNotFound_Returns404()
    {
        var response = await _client.GetAsync($"/v1/comments/{Guid.NewGuid()}/replies?limit=10");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("code").GetString().Should().Be("comment.not_found");
    }

    [Fact]
    public async Task ListReplies_InvalidLimit_Returns400()
    {
        var (ownerToken, username, channelHandle) = await CreateChannelAndGetIds();
        var viewerToken = await SignUpAndGetAccessToken();
        var videoId = await CreateReadyVideoAsync(ownerToken, username, channelHandle);
        var commentId = await AddCommentAsync(viewerToken, videoId, "Parent comment");

        var response = await _client.GetAsync($"/v1/comments/{commentId}/replies?limit=9999");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ListReplies_ValidRequest_Returns200WithReplies()
    {
        var (ownerToken, username, channelHandle) = await CreateChannelAndGetIds();
        var viewerToken = await SignUpAndGetAccessToken();
        var videoId = await CreateReadyVideoAsync(ownerToken, username, channelHandle);

        var parentId = await AddCommentAsync(viewerToken, videoId, "Parent comment");
        await AddCommentAsync(viewerToken, videoId, "First reply", parentId);
        await AddCommentAsync(viewerToken, videoId, "Second reply", parentId);

        var response = await _client.GetAsync($"/v1/comments/{parentId}/replies?limit=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var replies = body.GetProperty("data").GetProperty("replies").EnumerateArray().ToList();
        replies.Should().HaveCount(2);
        replies[0].GetProperty("content").GetString().Should().Be("First reply");
        replies[1].GetProperty("content").GetString().Should().Be("Second reply");
    }

    [Fact]
    public async Task ListReplies_WithCursor_ReturnsPaginatedResults()
    {
        var (ownerToken, username, channelHandle) = await CreateChannelAndGetIds();
        var viewerToken = await SignUpAndGetAccessToken();
        var videoId = await CreateReadyVideoAsync(ownerToken, username, channelHandle);

        var parentId = await AddCommentAsync(viewerToken, videoId, "Parent comment");
        await AddCommentAsync(viewerToken, videoId, "Reply 1", parentId);
        await AddCommentAsync(viewerToken, videoId, "Reply 2", parentId);
        await AddCommentAsync(viewerToken, videoId, "Reply 3", parentId);

        var firstPage = await _client.GetAsync($"/v1/comments/{parentId}/replies?limit=2");
        var firstBody = await firstPage.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var nextCursor = firstBody.GetProperty("data").GetProperty("nextCursor").GetString();

        nextCursor.Should().NotBeNull();

        var secondPage = await _client.GetAsync(
            $"/v1/comments/{parentId}/replies?limit=2&cursor={Uri.EscapeDataString(nextCursor!)}");
        var secondBody = await secondPage.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var secondReplies = secondBody.GetProperty("data").GetProperty("replies").EnumerateArray().ToList();

        secondReplies.Should().HaveCount(1);
        secondReplies[0].GetProperty("content").GetString().Should().Be("Reply 3");
    }

    [Fact]
    public async Task ListReplies_EmptyList_Returns200WithEmptyArray()
    {
        var (ownerToken, username, channelHandle) = await CreateChannelAndGetIds();
        var viewerToken = await SignUpAndGetAccessToken();
        var videoId = await CreateReadyVideoAsync(ownerToken, username, channelHandle);

        var parentId = await AddCommentAsync(viewerToken, videoId, "Parent comment with no replies");

        var response = await _client.GetAsync($"/v1/comments/{parentId}/replies?limit=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var replies = body.GetProperty("data").GetProperty("replies").EnumerateArray().ToList();
        replies.Should().BeEmpty();
    }

    private async Task<Guid> AddCommentAsync(string accessToken, Guid videoId, string content, Guid? parentCommentId = null)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        object body = parentCommentId.HasValue
            ? new { content, parentCommentId }
            : new { content };
        var response = await _client.PostAsJsonAsync($"/v1/videos/{videoId}/comments", body);
        var responseBody = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return Guid.Parse(responseBody.GetProperty("data").GetProperty("commentId").GetString()!);
    }

    private async Task<Guid> CreateReadyVideoAsync(string accessToken, string username, string channelHandle)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var createResponse = await _client.PostAsJsonAsync($"/v1/users/{username}/channels/{channelHandle}/videos", new
        {
            title = "Test Video",
            tags = Array.Empty<string>(),
            visibility = 0
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
