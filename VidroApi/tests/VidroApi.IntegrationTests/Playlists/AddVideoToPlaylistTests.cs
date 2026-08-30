using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using VidroApi.IntegrationTests.Common;

namespace VidroApi.IntegrationTests.Playlists;

public class AddVideoToPlaylistTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private const string WebhookSecret = "test-webhook-secret";
    private const string MinioUploadToken = "test-minio-upload-token";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Default)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task AddVideoToPlaylist_ValidVideo_Returns204()
    {
        var (token, _, username, channelHandle) = await CreateChannelAndGetIds();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var videoId = await CreateReadyVideo(username, channelHandle);
        var playlistId = await CreatePlaylist("My Playlist");

        var response = await _client.PostAsJsonAsync($"/v1/playlists/{playlistId}/items", new { videoId });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task AddVideoToPlaylist_VideoCount_Increments()
    {
        var (token, _, username, channelHandle) = await CreateChannelAndGetIds();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var videoId = await CreateReadyVideo(username, channelHandle);
        var playlistId = await CreatePlaylist("My Playlist");

        await _client.PostAsJsonAsync($"/v1/playlists/{playlistId}/items", new { videoId });

        _client.DefaultRequestHeaders.Authorization = null;
        var getResponse = await _client.GetAsync($"/v1/playlists/{playlistId}");
        var body = await getResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("data").GetProperty("videoCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task AddVideoToPlaylist_VideoAlreadyInPlaylist_Returns409WithExpectedCode()
    {
        var (token, _, username, channelHandle) = await CreateChannelAndGetIds();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var videoId = await CreateReadyVideo(username, channelHandle);
        var playlistId = await CreatePlaylist("My Playlist");

        await _client.PostAsJsonAsync($"/v1/playlists/{playlistId}/items", new { videoId });
        var response = await _client.PostAsJsonAsync($"/v1/playlists/{playlistId}/items", new { videoId });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("code").GetString().Should().Be("playlist.video_already_in_playlist");
    }

    [Fact]
    public async Task AddVideoToPlaylist_ChannelScopeVideoFromOtherChannel_Returns400WithExpectedCode()
    {
        var (token, channelId, username, _) = await CreateChannelAndGetIds();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await _client.PostAsJsonAsync("/v1/channels", new { handle = "other-channel", name = "Other Channel" });
        var otherVideoId = await CreateReadyVideo(username, "other-channel");

        var playlistResponse = await _client.PostAsJsonAsync("/v1/playlists", new
        {
            name = "Channel Playlist",
            visibility = 0,
            scope = 1,
            channelId
        });
        var playlistBody = await playlistResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var playlistId = Guid.Parse(playlistBody.GetProperty("data").GetProperty("playlistId").GetString()!);

        var response = await _client.PostAsJsonAsync($"/v1/playlists/{playlistId}/items", new { videoId = otherVideoId });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("code").GetString().Should().Be("playlist.video_not_from_channel");
    }

    [Fact]
    public async Task AddVideoToPlaylist_PlaylistNotFound_Returns404()
    {
        var token = await SignUpAndGetAccessToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync($"/v1/playlists/{Guid.NewGuid()}/items",
            new { videoId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AddVideoToPlaylist_WhenNotOwner_Returns403()
    {
        var (ownerToken, _, _, _) = await CreateChannelAndGetIds();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var playlistId = await CreatePlaylist("Owner Playlist");

        var otherToken = await SignUpAndGetAccessToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);

        var response = await _client.PostAsJsonAsync($"/v1/playlists/{playlistId}/items",
            new { videoId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AddVideoToPlaylist_WithoutAuth_Returns401()
    {
        var response = await _client.PostAsJsonAsync($"/v1/playlists/{Guid.NewGuid()}/items",
            new { videoId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<Guid> CreatePlaylist(string name)
    {
        var response = await _client.PostAsJsonAsync("/v1/playlists", new
        {
            name,
            visibility = 0,
            scope = 0
        });
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
