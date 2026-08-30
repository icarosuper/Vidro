using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using VidroApi.IntegrationTests.Common;

namespace VidroApi.IntegrationTests.Playlists;

public class UpdatePlaylistTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Default)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task UpdatePlaylist_AsOwner_Returns204()
    {
        var token = await SignUpAndGetAccessToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var playlistId = await CreatePlaylist("Old Name");

        var response = await _client.PutAsJsonAsync($"/v1/playlists/{playlistId}", new
        {
            name = "New Name",
            visibility = 1
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task UpdatePlaylist_AsOwner_UpdatesData()
    {
        var token = await SignUpAndGetAccessToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var playlistId = await CreatePlaylist("Old Name");

        await _client.PutAsJsonAsync($"/v1/playlists/{playlistId}", new
        {
            name = "Updated Name",
            description = "New description",
            visibility = 1
        });

        var getResponse = await _client.GetAsync($"/v1/playlists/{playlistId}");
        var body = await getResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        data.GetProperty("name").GetString().Should().Be("Updated Name");
        data.GetProperty("description").GetString().Should().Be("New description");
    }

    [Fact]
    public async Task UpdatePlaylist_WhenNotOwner_Returns403WithExpectedCode()
    {
        var ownerToken = await SignUpAndGetAccessToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var playlistId = await CreatePlaylist("Owner Playlist");

        var otherToken = await SignUpAndGetAccessToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);

        var response = await _client.PutAsJsonAsync($"/v1/playlists/{playlistId}", new
        {
            name = "Hacked Name",
            visibility = 0
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("code").GetString().Should().Be("playlist.not_owner");
    }

    [Fact]
    public async Task UpdatePlaylist_WithoutAuth_Returns401()
    {
        var response = await _client.PutAsJsonAsync($"/v1/playlists/{Guid.NewGuid()}", new
        {
            name = "Name",
            visibility = 0
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdatePlaylist_NonExistent_Returns404()
    {
        var token = await SignUpAndGetAccessToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PutAsJsonAsync($"/v1/playlists/{Guid.NewGuid()}", new
        {
            name = "Name",
            visibility = 0
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
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
}
