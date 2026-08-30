using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using VidroApi.IntegrationTests.Common;

namespace VidroApi.IntegrationTests.Channels;

public class UploadChannelAvatarTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Default)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task UploadChannelAvatar_WhenOwner_Returns200WithUploadUrl()
    {
        var (accessToken, _) = await CreateChannelAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.PostAsync("/v1/channels/test-channel/avatar", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        data.GetProperty("uploadUrl").GetString().Should().NotBeNullOrWhiteSpace();
        data.GetProperty("uploadExpiresAt").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task UploadChannelAvatar_WhenNotOwner_Returns404()
    {
        var (_, _) = await CreateChannelAndGetCredentials();
        var (nonOwnerToken, _) = await SignUpAndGetCredentials();

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", nonOwnerToken);
        var response = await _client.PostAsync("/v1/channels/test-channel/avatar", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UploadChannelAvatar_WithNonExistentHandle_Returns404()
    {
        var (accessToken, _) = await SignUpAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.PostAsync("/v1/channels/nonexistent/avatar", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UploadChannelAvatar_WhenNotAuthenticated_Returns401()
    {
        var (_, _) = await CreateChannelAndGetCredentials();

        var response = await _client.PostAsync("/v1/channels/test-channel/avatar", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetChannel_ReturnsAvatarUrl_AfterUpload()
    {
        var (accessToken, ownerUsername) = await CreateChannelAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        await _client.PostAsync("/v1/channels/test-channel/avatar", null);

        _client.DefaultRequestHeaders.Authorization = null;
        var getResponse = await _client.GetAsync($"/v1/users/{ownerUsername}/channels/test-channel");
        var body = await getResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var avatarUrl = body.GetProperty("data").GetProperty("avatarUrl").GetString();

        avatarUrl.Should().NotBeNullOrWhiteSpace();
    }

    private async Task<(string AccessToken, string Username)> CreateChannelAndGetCredentials()
    {
        var (accessToken, username) = await SignUpAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        await _client.PostAsJsonAsync("/v1/channels", new { handle = "test-channel", name = $"Channel {Guid.NewGuid()}" });

        _client.DefaultRequestHeaders.Authorization = null;
        return (accessToken, username);
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
}
