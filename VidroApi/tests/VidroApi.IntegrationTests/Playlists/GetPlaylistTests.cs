using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using VidroApi.IntegrationTests.Common;

namespace VidroApi.IntegrationTests.Playlists;

public class GetPlaylistTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Default)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task GetPlaylist_PublicPlaylist_Returns200WithData()
    {
        var token = await SignUpAndGetAccessToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var playlistId = await CreatePlaylist("My Playlist", 0);

        _client.DefaultRequestHeaders.Authorization = null;
        var response = await _client.GetAsync($"/v1/playlists/{playlistId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        data.GetProperty("playlistId").GetString().Should().Be(playlistId.ToString());
        data.GetProperty("name").GetString().Should().Be("My Playlist");
        data.GetProperty("videoCount").GetInt32().Should().Be(0);
        data.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task GetPlaylist_PrivatePlaylist_OwnerCanGet()
    {
        var token = await SignUpAndGetAccessToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var playlistId = await CreatePlaylist("Private Playlist", 1);

        var response = await _client.GetAsync($"/v1/playlists/{playlistId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetPlaylist_PrivatePlaylist_NonOwnerGets404()
    {
        var ownerToken = await SignUpAndGetAccessToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var playlistId = await CreatePlaylist("Private Playlist", 1);

        var otherToken = await SignUpAndGetAccessToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);
        var response = await _client.GetAsync($"/v1/playlists/{playlistId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetPlaylist_NonExistent_Returns404()
    {
        var response = await _client.GetAsync($"/v1/playlists/{Guid.NewGuid()}");

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

    private async Task<Guid> CreatePlaylist(string name, int visibility)
    {
        var response = await _client.PostAsJsonAsync("/v1/playlists", new
        {
            name,
            visibility,
            scope = 0
        });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return Guid.Parse(body.GetProperty("data").GetProperty("playlistId").GetString()!);
    }
}
