using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using VidroApi.IntegrationTests.Common;

namespace VidroApi.IntegrationTests.Videos;

public class MinioUploadCompletedTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private const string MinioUploadToken = "test-minio-upload-token";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Default)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task MinioUploadCompleted_WithValidToken_AndUploadEvent_Returns200()
    {
        var (_, videoId) = await CreateVideoAndGetIds();

        var response = await SendMinioWebhookAsync(
            eventName: "s3:ObjectCreated:Put",
            key: $"test-bucket/raw/{videoId}",
            token: MinioUploadToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MinioUploadCompleted_WithInvalidToken_Returns401()
    {
        var (_, videoId) = await CreateVideoAndGetIds();

        var response = await SendMinioWebhookAsync(
            eventName: "s3:ObjectCreated:Put",
            key: $"test-bucket/raw/{videoId}",
            token: "wrong-token");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MinioUploadCompleted_WithNonUploadEvent_Returns200AndIsIgnored()
    {
        var (_, videoId) = await CreateVideoAndGetIds();

        var response = await SendMinioWebhookAsync(
            eventName: "s3:ObjectRemoved:Delete",
            key: $"test-bucket/raw/{videoId}",
            token: MinioUploadToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MinioUploadCompleted_WithKeyMissingRawSegment_Returns200AndIsIgnored()
    {
        var response = await SendMinioWebhookAsync(
            eventName: "s3:ObjectCreated:Put",
            key: "test-bucket/other/some-file",
            token: MinioUploadToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MinioUploadCompleted_WithNonGuidVideoId_Returns200AndIsIgnored()
    {
        var response = await SendMinioWebhookAsync(
            eventName: "s3:ObjectCreated:Put",
            key: "test-bucket/raw/not-a-guid",
            token: MinioUploadToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MinioUploadCompleted_WithNonExistentVideoId_Returns200AndIsIgnored()
    {
        var response = await SendMinioWebhookAsync(
            eventName: "s3:ObjectCreated:Put",
            key: $"test-bucket/raw/{Guid.NewGuid()}",
            token: MinioUploadToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task MinioUploadCompleted_WhenCalledTwice_Returns200BothTimes()
    {
        var (_, videoId) = await CreateVideoAndGetIds();

        var firstResponse = await SendMinioWebhookAsync(
            eventName: "s3:ObjectCreated:Put",
            key: $"test-bucket/raw/{videoId}",
            token: MinioUploadToken);

        var secondResponse = await SendMinioWebhookAsync(
            eventName: "s3:ObjectCreated:Put",
            key: $"test-bucket/raw/{videoId}",
            token: MinioUploadToken);

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<HttpResponseMessage> SendMinioWebhookAsync(string eventName, string key, string token)
    {
        var payload = JsonSerializer.Serialize(new { EventName = eventName, Key = key });
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/minio-upload-completed")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private async Task<(string AccessToken, Guid VideoId)> CreateVideoAndGetIds()
    {
        var (accessToken, username, channelHandle) = await CreateChannelAndGetIds();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var createResponse = await _client.PostAsJsonAsync($"/v1/users/{username}/channels/{channelHandle}/videos", new
        {
            title = "Upload Test Video",
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
