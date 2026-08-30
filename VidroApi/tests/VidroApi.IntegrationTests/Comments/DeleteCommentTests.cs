using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using VidroApi.IntegrationTests.Common;

namespace VidroApi.IntegrationTests.Comments;

public class DeleteCommentTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private const string WebhookSecret = "test-webhook-secret";
    private const string MinioUploadToken = "test-minio-upload-token";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Default)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task DeleteComment_WithoutAuthentication_Returns401()
    {
        var response = await _client.DeleteAsync($"/v1/comments/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteComment_CommentNotFound_Returns404()
    {
        var accessToken = await SignUpAndGetAccessToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.DeleteAsync($"/v1/comments/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("code").GetString().Should().Be("comment.not_found");
    }

    [Fact]
    public async Task DeleteComment_NotOwner_Returns403()
    {
        var (ownerToken, username, channelHandle) = await CreateChannelAndGetIds();
        var viewerToken = await SignUpAndGetAccessToken();
        var anotherToken = await SignUpAndGetAccessToken();
        var videoId = await CreateReadyVideoAsync(ownerToken, username, channelHandle);

        var commentId = await AddCommentAsync(viewerToken, videoId, "Viewer's comment");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", anotherToken);
        var response = await _client.DeleteAsync($"/v1/comments/{commentId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("code").GetString().Should().Be("comment.not_owner");
    }

    [Fact]
    public async Task DeleteComment_ValidDelete_Returns204AndDecrementsVideoCommentCount()
    {
        var (ownerToken, username, channelHandle) = await CreateChannelAndGetIds();
        var viewerToken = await SignUpAndGetAccessToken();
        var videoId = await CreateReadyVideoAsync(ownerToken, username, channelHandle);

        var commentId = await AddCommentAsync(viewerToken, videoId, "To be deleted");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", viewerToken);
        var response = await _client.DeleteAsync($"/v1/comments/{commentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        _client.DefaultRequestHeaders.Authorization = null;
        var videoResponse = await _client.GetAsync($"/v1/videos/{videoId}");
        var videoBody = await videoResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        videoBody.GetProperty("data").GetProperty("commentCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task DeleteComment_Reply_DecrementParentReplyCount()
    {
        var (ownerToken, username, channelHandle) = await CreateChannelAndGetIds();
        var viewerToken = await SignUpAndGetAccessToken();
        var videoId = await CreateReadyVideoAsync(ownerToken, username, channelHandle);

        var parentCommentId = await AddCommentAsync(viewerToken, videoId, "Parent comment");
        var replyId = await AddCommentAsync(viewerToken, videoId, "Reply", parentCommentId);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", viewerToken);
        await _client.DeleteAsync($"/v1/comments/{replyId}");

        var replyCount = await GetReplyCount(videoId, parentCommentId);
        replyCount.Should().Be(0);
    }

    [Fact]
    public async Task DeleteComment_SoftDeletes_CommentStillAppearsInList()
    {
        var (ownerToken, username, channelHandle) = await CreateChannelAndGetIds();
        var viewerToken = await SignUpAndGetAccessToken();
        var videoId = await CreateReadyVideoAsync(ownerToken, username, channelHandle);

        var commentId = await AddCommentAsync(viewerToken, videoId, "Will be soft deleted");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", viewerToken);
        await _client.DeleteAsync($"/v1/comments/{commentId}");

        _client.DefaultRequestHeaders.Authorization = null;
        var listResponse = await _client.GetAsync($"/v1/videos/{videoId}/comments?sort=Recent&limit=50");
        var body = await listResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var comments = body.GetProperty("data").GetProperty("comments").EnumerateArray().ToList();

        var deletedComment = comments.FirstOrDefault(c => c.GetProperty("commentId").GetString() == commentId.ToString());
        deletedComment.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        deletedComment.GetProperty("isDeleted").GetBoolean().Should().BeTrue();
        deletedComment.GetProperty("content").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private async Task<int> GetReplyCount(Guid videoId, Guid commentId)
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var response = await _client.GetAsync($"/v1/videos/{videoId}/comments?sort=Recent&limit=50");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var comments = body.GetProperty("data").GetProperty("comments").EnumerateArray();
        var comment = comments.First(c => c.GetProperty("commentId").GetString() == commentId.ToString());
        return comment.GetProperty("replyCount").GetInt32();
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
