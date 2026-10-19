using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VidroApi.Infrastructure.Persistence;
using VidroApi.Infrastructure.Services;
using VidroApi.IntegrationTests.Common;

namespace VidroApi.IntegrationTests.Channels;

public class DeleteChannelTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Default)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task DeleteChannel_AsOwner_Returns204()
    {
        var (accessToken, _) = await SignUpAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        await CreateChannel("test-channel", "My Channel");

        var response = await _client.DeleteAsync("/v1/channels/test-channel");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteChannel_AsOwner_StagesRawAndRawArchivedCleanupForVideos()
    {
        var (accessToken, username) = await SignUpAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        await CreateChannel("test-channel", "My Channel");

        var createResponse = await _client.PostAsJsonAsync(
            $"/v1/users/{username}/channels/test-channel/videos",
            new { title = "Video to Delete", tags = Array.Empty<string>(), visibility = 0 });
        var createBody = await createResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var videoId = Guid.Parse(createBody.GetProperty("data").GetProperty("videoId").GetString()!);

        var response = await _client.DeleteAsync("/v1/channels/test-channel");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stagedPaths = await db.PendingStorageCleanups
            .Select(cleanup => cleanup.ObjectPath)
            .ToListAsync();

        stagedPaths.Should().Contain(MinioObjectKeys.Raw(videoId));
        stagedPaths.Should().Contain(MinioObjectKeys.RawArchived(videoId),
            "after processing the raw lives in raw-archived/; archive can also fail and leave it in raw/");
    }

    [Fact]
    public async Task DeleteChannel_ChannelNoLongerExistsAfterDeletion()
    {
        var (accessToken, ownerUsername) = await SignUpAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        await CreateChannel("test-channel", "My Channel");

        await _client.DeleteAsync("/v1/channels/test-channel");

        _client.DefaultRequestHeaders.Authorization = null;
        var getResponse = await _client.GetAsync($"/v1/users/{ownerUsername}/channels/test-channel");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteChannel_WithoutAuth_Returns401()
    {
        var (accessToken, _) = await SignUpAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        await CreateChannel("test-channel", "My Channel");

        _client.DefaultRequestHeaders.Authorization = null;
        var response = await _client.DeleteAsync("/v1/channels/test-channel");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteChannel_WhenNotOwner_Returns404()
    {
        var (ownerToken, _) = await SignUpAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        await CreateChannel("owner-channel", "Owner Channel");

        var (otherToken, _) = await SignUpAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);
        var response = await _client.DeleteAsync("/v1/channels/owner-channel");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteChannel_WithNonExistentHandle_Returns404()
    {
        var (accessToken, _) = await SignUpAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.DeleteAsync("/v1/channels/nonexistent");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<(string AccessToken, string Username)> SignUpAndGetCredentials()
    {
        var username = $"usr{Guid.NewGuid():N}"[..15];
        var email = $"user_{Guid.NewGuid():N}@example.com";
        var password = "StrongPass1!";

        await _client.PostAsJsonAsync("/v1/auth/signup", new { username, email, password });

        var signInResponse = await _client.PostAsJsonAsync("/v1/auth/signin", new { email, password });
        var body = await signInResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var accessToken = body.GetProperty("data").GetProperty("accessToken").GetString()!;

        return (accessToken, username);
    }

    private async Task CreateChannel(string handle, string name)
    {
        await _client.PostAsJsonAsync("/v1/channels", new { handle, name });
    }
}
