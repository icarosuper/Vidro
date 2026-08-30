using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using VidroApi.IntegrationTests.Common;

namespace VidroApi.IntegrationTests.Auth;

public class SignOutTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Default)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task SignOut_WithValidToken_Returns204()
    {
        var (accessToken, refreshToken) = await SignInAndGetTokens();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.PostAsJsonAsync("/v1/auth/signout", new { refreshToken });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task SignOut_RevokesToken_SubsequentRenewFails()
    {
        var (accessToken, refreshToken) = await SignInAndGetTokens();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        await _client.PostAsJsonAsync("/v1/auth/signout", new { refreshToken });

        var renewResponse = await _client.PostAsJsonAsync("/v1/auth/renew-token", new { refreshToken });
        renewResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SignOut_WithTokenBelongingToAnotherUser_Returns401()
    {
        var (_, otherUserRefreshToken) = await SignInAndGetTokens();
        var (accessToken, _) = await SignInAndGetTokens();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.PostAsJsonAsync("/v1/auth/signout", new { refreshToken = otherUserRefreshToken });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SignOut_WithEmptyToken_Returns400()
    {
        var (accessToken, _) = await SignInAndGetTokens();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.PostAsJsonAsync("/v1/auth/signout", new { refreshToken = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SignOut_WithoutAccessToken_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/v1/auth/signout", new { refreshToken = "any-token" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<(string AccessToken, string RefreshToken)> SignInAndGetTokens()
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
        var data = body.GetProperty("data");

        return (
            data.GetProperty("accessToken").GetString()!,
            data.GetProperty("refreshToken").GetString()!
        );
    }
}
