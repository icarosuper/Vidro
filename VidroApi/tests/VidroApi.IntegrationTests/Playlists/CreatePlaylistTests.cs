using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using VidroApi.IntegrationTests.Common;

namespace VidroApi.IntegrationTests.Playlists;

public class CreatePlaylistTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Default)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task CreatePlaylist_UserScope_Returns201WithPlaylistId()
    {
        var token = await SignUpAndGetAccessToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync("/v1/playlists", new
        {
            name = "My Playlist",
            visibility = 0,
            scope = 0
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("data").GetProperty("playlistId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CreatePlaylist_ChannelScope_Returns201()
    {
        var (token, channelId, _) = await CreateChannelAndGetIds();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync("/v1/playlists", new
        {
            name = "Channel Playlist",
            visibility = 0,
            scope = 1,
            channelId
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreatePlaylist_ChannelScopeWithoutChannelId_Returns400()
    {
        var token = await SignUpAndGetAccessToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync("/v1/playlists", new
        {
            name = "Channel Playlist",
            visibility = 0,
            scope = 1
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreatePlaylist_ChannelScopeWithOtherUsersChannel_Returns404()
    {
        var (otherToken, channelId, otherUsername) = await CreateChannelAndGetIds();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);

        var myToken = await SignUpAndGetAccessToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", myToken);

        var response = await _client.PostAsJsonAsync("/v1/playlists", new
        {
            name = "My Playlist",
            visibility = 0,
            scope = 1,
            channelId
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreatePlaylist_WithoutAuth_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/v1/playlists", new
        {
            name = "My Playlist",
            visibility = 0,
            scope = 0
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreatePlaylist_WithEmptyName_Returns400()
    {
        var token = await SignUpAndGetAccessToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync("/v1/playlists", new
        {
            name = "",
            visibility = 0,
            scope = 0
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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

    private async Task<(string Token, Guid ChannelId, string Username)> CreateChannelAndGetIds()
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

        return (token, channelId, username);
    }
}
