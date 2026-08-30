using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using VidroApi.IntegrationTests.Common;

namespace VidroApi.IntegrationTests.Videos;

public class UploadVideoThumbnailTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Default)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task UploadVideoThumbnail_WhenOwner_Returns200WithUploadUrl()
    {
        var (accessToken, videoId) = await CreateVideoAndGetIds();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.PostAsync($"/v1/videos/{videoId}/thumbnail", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        data.GetProperty("uploadUrl").GetString().Should().NotBeNullOrWhiteSpace();
        data.GetProperty("uploadExpiresAt").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task UploadVideoThumbnail_WhenNotAuthenticated_Returns401()
    {
        var response = await _client.PostAsync($"/v1/videos/{Guid.NewGuid()}/thumbnail", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UploadVideoThumbnail_WhenNotOwner_Returns403()
    {
        var (_, videoId) = await CreateVideoAndGetIds();
        var otherToken = await SignUpAndGetAccessToken();

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);

        var response = await _client.PostAsync($"/v1/videos/{videoId}/thumbnail", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UploadVideoThumbnail_WhenVideoNotFound_Returns404()
    {
        var accessToken = await SignUpAndGetAccessToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.PostAsync($"/v1/videos/{Guid.NewGuid()}/thumbnail", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<(string AccessToken, Guid VideoId)> CreateVideoAndGetIds()
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

        var videoResponse = await _client.PostAsJsonAsync($"/v1/users/{username}/channels/test-channel/videos", new
        {
            title = "My Video",
            tags = Array.Empty<string>(),
            visibility = 0
        });
        var videoBody = await videoResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var videoId = Guid.Parse(videoBody.GetProperty("data").GetProperty("videoId").GetString()!);

        return (accessToken, videoId);
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
