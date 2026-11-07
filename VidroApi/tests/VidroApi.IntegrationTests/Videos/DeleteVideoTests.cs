using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VidroApi.Infrastructure.Persistence;
using VidroApi.Infrastructure.Services;
using VidroApi.IntegrationTests.Common;

namespace VidroApi.IntegrationTests.Videos;

public class DeleteVideoTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private const string WebhookSecret = "test-webhook-secret";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Default)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task DeleteVideo_AsOwner_Returns204()
    {
        var (accessToken, videoId) = await CreateReadyVideo();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.DeleteAsync($"/v1/videos/{videoId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteVideo_AsOwner_StagesRawAndRawArchivedCleanup()
    {
        var (accessToken, videoId) = await CreateReadyVideo();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.DeleteAsync($"/v1/videos/{videoId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stagedPaths = await db.PendingStorageCleanups
            .Select(cleanup => cleanup.ObjectPath)
            .ToListAsync();

        stagedPaths.Should().Contain(MinioObjectKeys.Raw(videoId),
            "a job that failed to archive still has the object in raw/");
        stagedPaths.Should().Contain(MinioObjectKeys.RawArchived(videoId),
            "after a successful process the raw lives in raw-archived/, not raw/");
    }

    [Fact]
    public async Task DeleteVideo_InPlaylist_RemovesTheItemAndDecrementsVideoCount()
    {
        var (accessToken, videoId) = await CreateReadyVideo();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var playlistId = await CreatePlaylistWith(videoId);

        var response = await _client.DeleteAsync($"/v1/videos/{videoId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var playlistResponse = await _client.GetAsync($"/v1/playlists/{playlistId}");
        var playlist = (await playlistResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions)).GetProperty("data");
        playlist.GetProperty("videoCount").GetInt32().Should().Be(0,
            "the count follows the items: a deleted video leaves every playlist it was in");
        playlist.GetProperty("items").GetArrayLength().Should().Be(0);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var itemRowsLeft = await db.PlaylistItems.CountAsync(item => item.PlaylistId == playlistId);
        itemRowsLeft.Should().Be(0, "the item row is deleted with the video, not kept as an orphan");
    }

    [Fact]
    public async Task DeleteVideo_VideoNoLongerAccessibleAfterDeletion()
    {
        var (accessToken, videoId) = await CreateReadyVideo();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        await _client.DeleteAsync($"/v1/videos/{videoId}");

        var getResponse = await _client.GetAsync($"/v1/videos/{videoId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteVideo_WithoutAuthentication_Returns401()
    {
        var (_, videoId) = await CreateReadyVideo();
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.DeleteAsync($"/v1/videos/{videoId}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteVideo_AsNonOwner_Returns403()
    {
        var (_, videoId) = await CreateReadyVideo();

        var otherToken = await SignUpAndGetAccessToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);

        var response = await _client.DeleteAsync($"/v1/videos/{videoId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteVideo_PrivateVideo_AsNonOwner_Returns404()
    {
        var (_, videoId) = await CreateReadyVideo(visibility: 2); // Private

        var otherToken = await SignUpAndGetAccessToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);

        var response = await _client.DeleteAsync($"/v1/videos/{videoId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteVideo_NonExistentVideo_Returns404()
    {
        var accessToken = await SignUpAndGetAccessToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.DeleteAsync($"/v1/videos/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<(string AccessToken, Guid VideoId)> CreateReadyVideo(int visibility = 0)
    {
        var (accessToken, username, channelHandle) = await CreateChannelAndGetIds();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var createResponse = await _client.PostAsJsonAsync($"/v1/users/{username}/channels/{channelHandle}/videos", new
        {
            title = "Video to Delete",
            tags = Array.Empty<string>(),
            visibility
        });
        var createBody = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var videoId = Guid.Parse(createBody.GetProperty("data").GetProperty("videoId").GetString()!);

        await SimulateProcessingCompletedAsync(videoId);

        return (accessToken, videoId);
    }

    private async Task<Guid> CreatePlaylistWith(Guid videoId)
    {
        var createResponse = await _client.PostAsJsonAsync("/v1/playlists", new
        {
            name = "Playlist with the video",
            visibility = 0,
            scope = 0
        });
        var createBody = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var playlistId = Guid.Parse(createBody.GetProperty("data").GetProperty("playlistId").GetString()!);

        var addResponse = await _client.PostAsJsonAsync($"/v1/playlists/{playlistId}/items", new { videoId });
        addResponse.EnsureSuccessStatusCode();
        return playlistId;
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
