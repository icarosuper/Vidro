using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using VidroApi.IntegrationTests.Common;

namespace VidroApi.IntegrationTests.Auth;

public class RenewTokenTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Default)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task Refresh_WithValidToken_Returns200WithNewTokens()
    {
        var refreshToken = await SignInAndGetRefreshToken();

        var response = await _client.PostAsJsonAsync("/v1/auth/renew-token", new { refreshToken });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        data.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();
        data.GetProperty("refreshToken").GetString().Should().NotBeNullOrWhiteSpace();
        data.GetProperty("secondsToExpiration").GetInt32().Should().BePositive();
    }

    [Fact]
    public async Task Refresh_RotatesToken_OldTokenIsRevoked()
    {
        var refreshToken = await SignInAndGetRefreshToken();

        await _client.PostAsJsonAsync("/v1/auth/renew-token", new { refreshToken });

        var response = await _client.PostAsJsonAsync("/v1/auth/renew-token", new { refreshToken });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("code").GetString().Should().Be("refresh_token.revoked");
    }

    [Fact]
    public async Task Refresh_WithNewToken_ReturnsNewValidTokens()
    {
        var firstRefreshToken = await SignInAndGetRefreshToken();

        var firstRefreshResponse = await _client.PostAsJsonAsync("/v1/auth/renew-token", new { refreshToken = firstRefreshToken });
        var firstBody = await firstRefreshResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var secondRefreshToken = firstBody.GetProperty("data").GetProperty("refreshToken").GetString()!;

        var response = await _client.PostAsJsonAsync("/v1/auth/renew-token", new { refreshToken = secondRefreshToken });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refresh_WithNonExistentToken_Returns401WithExpectedCode()
    {
        var response = await _client.PostAsJsonAsync("/v1/auth/renew-token", new { refreshToken ="token-that-does-not-exist" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("code").GetString().Should().Be("refresh_token.not_found");
    }

    [Fact]
    public async Task Refresh_WithEmptyToken_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/v1/auth/renew-token", new { refreshToken = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<string> SignInAndGetRefreshToken()
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
        return body.GetProperty("data").GetProperty("refreshToken").GetString()!;
    }
}
