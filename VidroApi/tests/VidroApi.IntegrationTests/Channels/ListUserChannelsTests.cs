using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using VidroApi.IntegrationTests.Common;

namespace VidroApi.IntegrationTests.Channels;

public class ListUserChannelsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Default)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task ListUserChannels_WithValidUsername_Returns200WithChannelsList()
    {
        var (accessToken, username) = await SignUpAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        await _client.PostAsJsonAsync("/v1/channels", new { handle = "channel-1", name = "Channel 1", description = "First channel" });
        await _client.PostAsJsonAsync("/v1/channels", new { handle = "channel-2", name = "Channel 2", description = "Second channel" });

        _client.DefaultRequestHeaders.Authorization = null;
        var response = await _client.GetAsync($"/v1/users/{username}/channels");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var channels = body.GetProperty("data").GetProperty("channels");
        channels.GetArrayLength().Should().Be(2);
        channels[0].GetProperty("handle").GetString().Should().Be("channel-1");
        channels[0].GetProperty("name").GetString().Should().Be("Channel 1");
        channels[0].GetProperty("description").GetString().Should().Be("First channel");
        channels[1].GetProperty("handle").GetString().Should().Be("channel-2");
        channels[1].GetProperty("name").GetString().Should().Be("Channel 2");
        channels[1].GetProperty("description").GetString().Should().Be("Second channel");
    }

    [Fact]
    public async Task ListUserChannels_WithNoChannels_Returns200WithEmptyList()
    {
        var (_, username) = await SignUpAndGetCredentials();

        var response = await _client.GetAsync($"/v1/users/{username}/channels");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var channels = body.GetProperty("data").GetProperty("channels");
        channels.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ListUserChannels_WithNonExistentUsername_Returns404()
    {
        var response = await _client.GetAsync("/v1/users/nobody/channels");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListUserChannels_IsPublic_NoAuthRequired()
    {
        var (accessToken, username) = await SignUpAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        await _client.PostAsJsonAsync("/v1/channels", new { handle = "test-channel", name = "Test Channel" });

        _client.DefaultRequestHeaders.Authorization = null;
        var response = await _client.GetAsync($"/v1/users/{username}/channels");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ListUserChannels_OnlyListsChannelsForSpecificUser()
    {
        var (accessToken1, username1) = await SignUpAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken1);
        await _client.PostAsJsonAsync("/v1/channels", new { handle = "user1-channel", name = "User1 Channel" });

        var (accessToken2, username2) = await SignUpAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken2);
        await _client.PostAsJsonAsync("/v1/channels", new { handle = "user2-channel", name = "User2 Channel" });

        _client.DefaultRequestHeaders.Authorization = null;
        var response = await _client.GetAsync($"/v1/users/{username1}/channels");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var channels = body.GetProperty("data").GetProperty("channels");
        channels.GetArrayLength().Should().Be(1);
        channels[0].GetProperty("name").GetString().Should().Be("User1 Channel");
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
