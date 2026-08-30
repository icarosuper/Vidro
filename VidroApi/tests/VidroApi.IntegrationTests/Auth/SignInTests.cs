using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using VidroApi.IntegrationTests.Common;

namespace VidroApi.IntegrationTests.Auth;

public class SignInTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Default)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task SignIn_WithValidCredentials_Returns200WithTokens()
    {
        var email = $"user_{Guid.NewGuid():N}@example.com";
        var password = "StrongPass1!";
        await RegisterUser($"usr{Guid.NewGuid():N}"[..15], email, password);

        var request = new { email, password };
        var response = await _client.PostAsJsonAsync("/v1/auth/signin", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");
        data.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();
        data.GetProperty("refreshToken").GetString().Should().NotBeNullOrWhiteSpace();
        data.GetProperty("secondsToExpiration").GetInt32().Should().BePositive();
    }

    [Fact]
    public async Task SignIn_WithNonExistentEmail_Returns401WithExpectedCode()
    {
        var request = new
        {
            email = $"ghost_{Guid.NewGuid():N}@example.com",
            password = "StrongPass1!"
        };

        var response = await _client.PostAsJsonAsync("/v1/auth/signin", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("code").GetString().Should().Be("user.invalid_credentials");
    }

    [Fact]
    public async Task SignIn_WithWrongPassword_Returns401WithExpectedCode()
    {
        var email = $"user_{Guid.NewGuid():N}@example.com";
        await RegisterUser($"usr{Guid.NewGuid():N}"[..15], email, "CorrectPass1!");

        var request = new { email, password = "WrongPass1!" };
        var response = await _client.PostAsJsonAsync("/v1/auth/signin", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("code").GetString().Should().Be("user.invalid_credentials");
    }

    [Theory]
    [InlineData("", "StrongPass1!")]             // empty email
    [InlineData("not-an-email", "StrongPass1!")] // invalid email format
    [InlineData("user@example.com", "")]         // empty password
    public async Task SignIn_WithInvalidData_Returns400(string email, string password)
    {
        var request = new { email, password };

        var response = await _client.PostAsJsonAsync("/v1/auth/signin", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task RegisterUser(string username, string email, string password)
    {
        var request = new { username, email, password };
        var response = await _client.PostAsJsonAsync("/v1/auth/signup", request);
        response.EnsureSuccessStatusCode();
    }
}
