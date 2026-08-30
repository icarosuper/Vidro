using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using VidroApi.IntegrationTests.Common;

namespace VidroApi.IntegrationTests.Channels;

public class FollowChannelTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Default)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task FollowChannel_WithValidChannel_Returns204()
    {
        var (ownerUsername, channelHandle) = await CreateChannelAsNewUser();
        var (followerToken, _) = await SignUpAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", followerToken);

        var response = await _client.PostAsync($"/v1/users/{ownerUsername}/channels/{channelHandle}/follow", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task FollowChannel_IncrementsFollowerCount()
    {
        var (ownerUsername, channelHandle) = await CreateChannelAsNewUser();
        var (followerToken, _) = await SignUpAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", followerToken);

        await _client.PostAsync($"/v1/users/{ownerUsername}/channels/{channelHandle}/follow", null);

        _client.DefaultRequestHeaders.Authorization = null;
        var getResponse = await _client.GetAsync($"/v1/users/{ownerUsername}/channels/{channelHandle}");
        var body = await getResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);

        body.GetProperty("data").GetProperty("followerCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task FollowChannel_WhenAlreadyFollowing_Returns409WithExpectedCode()
    {
        var (ownerUsername, channelHandle) = await CreateChannelAsNewUser();
        var (followerToken, _) = await SignUpAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", followerToken);

        await _client.PostAsync($"/v1/users/{ownerUsername}/channels/{channelHandle}/follow", null);
        var response = await _client.PostAsync($"/v1/users/{ownerUsername}/channels/{channelHandle}/follow", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("code").GetString().Should().Be("channel.already_following");
    }

    [Fact]
    public async Task FollowChannel_WithoutAuth_Returns401()
    {
        var (ownerUsername, channelHandle) = await CreateChannelAsNewUser();

        var response = await _client.PostAsync($"/v1/users/{ownerUsername}/channels/{channelHandle}/follow", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task FollowChannel_OwnChannel_Returns409WithExpectedCode()
    {
        var (accessToken, username) = await SignUpAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        await _client.PostAsJsonAsync("/v1/channels", new { handle = "test-channel", name = "My Channel" });

        var response = await _client.PostAsync($"/v1/users/{username}/channels/test-channel/follow", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("code").GetString().Should().Be("channel.cannot_follow_own");
    }

    [Fact]
    public async Task FollowChannel_WithNonExistentHandle_Returns404()
    {
        var (ownerUsername, _) = await CreateChannelAsNewUser();
        var (followerToken, _) = await SignUpAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", followerToken);

        var response = await _client.PostAsync($"/v1/users/{ownerUsername}/channels/nonexistent/follow", null);

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

    private async Task<(string OwnerUsername, string ChannelHandle)> CreateChannelAsNewUser()
    {
        var (accessToken, username) = await SignUpAndGetCredentials();
        var previousAuth = _client.DefaultRequestHeaders.Authorization;
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        await _client.PostAsJsonAsync("/v1/channels", new { handle = "a-channel", name = "A Channel" });

        _client.DefaultRequestHeaders.Authorization = previousAuth;
        return (username, "a-channel");
    }
}
