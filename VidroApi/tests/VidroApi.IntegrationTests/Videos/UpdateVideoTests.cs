using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using VidroApi.IntegrationTests.Common;

namespace VidroApi.IntegrationTests.Videos;

public class UpdateVideoTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Default)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task UpdateVideo_WithValidData_Returns200WithUpdatedInfo()
    {
        var (accessToken, videoId) = await CreateVideoAndGetIds();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.PutAsJsonAsync($"/v1/videos/{videoId}", new
        {
            title = "Updated Video Title",
            description = "Updated description",
            tags = new[] { "updated", "tags" },
            visibility = 1 // Unlisted
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        data.GetProperty("videoId").GetString().Should().NotBeNullOrWhiteSpace();
        data.GetProperty("title").GetString().Should().Be("Updated Video Title");
        data.GetProperty("description").GetString().Should().Be("Updated description");
        data.GetProperty("tags")[0].GetString().Should().Be("updated");
        data.GetProperty("tags")[1].GetString().Should().Be("tags");
        data.GetProperty("visibility").GetProperty("value").GetString().Should().Be("Unlisted");
    }

    [Fact]
    public async Task UpdateVideo_WithoutAuthentication_Returns401()
    {
        var videoId = Guid.NewGuid();

        var response = await _client.PutAsJsonAsync($"/v1/videos/{videoId}", new
        {
            title = "Updated Title",
            tags = Array.Empty<string>(),
            visibility = 0
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateVideo_OnVideoNotOwned_Returns403()
    {
        var (accessToken1, videoId) = await CreateVideoAndGetIds();
        var accessToken2 = await SignUpAndGetAccessToken();

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken2);

        var response = await _client.PutAsJsonAsync($"/v1/videos/{videoId}", new
        {
            title = "Hacked Title",
            tags = Array.Empty<string>(),
            visibility = 0
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateVideo_OnNonExistentVideo_Returns404()
    {
        var accessToken = await SignUpAndGetAccessToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.PutAsJsonAsync($"/v1/videos/{Guid.NewGuid()}", new
        {
            title = "Updated Title",
            tags = Array.Empty<string>(),
            visibility = 0
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateVideo_WithEmptyTitle_Returns400()
    {
        var (accessToken, videoId) = await CreateVideoAndGetIds();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.PutAsJsonAsync($"/v1/videos/{videoId}", new
        {
            title = "",
            tags = Array.Empty<string>(),
            visibility = 0
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateVideo_WithTitleTooLong_Returns400()
    {
        var (accessToken, videoId) = await CreateVideoAndGetIds();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.PutAsJsonAsync($"/v1/videos/{videoId}", new
        {
            title = new string('a', 201),
            tags = Array.Empty<string>(),
            visibility = 0
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateVideo_WithDescriptionTooLong_Returns400()
    {
        var (accessToken, videoId) = await CreateVideoAndGetIds();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.PutAsJsonAsync($"/v1/videos/{videoId}", new
        {
            title = "Valid Title",
            description = new string('a', 2001),
            tags = Array.Empty<string>(),
            visibility = 0
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateVideo_WithTooManyTags_Returns400()
    {
        var (accessToken, videoId) = await CreateVideoAndGetIds();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.PutAsJsonAsync($"/v1/videos/{videoId}", new
        {
            title = "Valid Title",
            tags = Enumerable.Range(1, 11).Select(i => $"tag{i}").ToArray(),
            visibility = 0
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<(string AccessToken, Guid VideoId)> CreateVideoAndGetIds()
    {
        var (accessToken, username, channelHandle) = await CreateChannelAndGetIds();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var videoResponse = await _client.PostAsJsonAsync($"/v1/users/{username}/channels/{channelHandle}/videos", new
        {
            title = "Original Video",
            description = "Original description",
            tags = new[] { "original" },
            visibility = 0
        });

        var videoBody = await videoResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var videoId = Guid.Parse(videoBody.GetProperty("data").GetProperty("videoId").GetString()!);

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
