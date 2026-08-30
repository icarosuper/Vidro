using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using VidroApi.IntegrationTests.Common;

namespace VidroApi.IntegrationTests.Users;

public class GetCurrentUserTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Default)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task GetCurrentUser_WhenAuthenticated_Returns200WithUserData()
    {
        var (accessToken, username, email) = await SignUpAndGetAccessToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.GetAsync("/v1/users/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        data.GetProperty("username").GetString().Should().Be(username);
        data.GetProperty("email").GetString().Should().Be(email);
        data.GetProperty("userId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task GetCurrentUser_WhenNotAuthenticated_Returns401()
    {
        var response = await _client.GetAsync("/v1/users/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetCurrentUser_WhenAuthenticated_ReturnsNullAvatarUrlIfNotSet()
    {
        var (accessToken, _, _) = await SignUpAndGetAccessToken();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.GetAsync("/v1/users/me");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        data.GetProperty("avatarUrl").ValueKind.Should().Be(JsonValueKind.Null);
    }

    private async Task<(string accessToken, string username, string email)> SignUpAndGetAccessToken()
    {
        var email = $"user_{Guid.NewGuid():N}@example.com";
        var password = "StrongPass1!";
        var username = $"usr{Guid.NewGuid():N}"[..15];

        await _client.PostAsJsonAsync("/v1/auth/signup", new
        {
            username,
            email,
            password
        });

        var signInResponse = await _client.PostAsJsonAsync("/v1/auth/signin", new { email, password });
        var body = await signInResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var accessToken = body.GetProperty("data").GetProperty("accessToken").GetString()!;

        return (accessToken, username, email);
    }
}
