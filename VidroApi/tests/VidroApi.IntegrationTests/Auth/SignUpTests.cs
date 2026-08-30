using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using VidroApi.IntegrationTests.Common;

namespace VidroApi.IntegrationTests.Auth;

public class SignUpTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Default)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task SignUp_WithValidData_Returns201WithUserId()
    {
        var request = new
        {
            username = $"user_{Guid.NewGuid():N}"[..20],
            email = $"user_{Guid.NewGuid():N}@example.com",
            password = "StrongPass1!"
        };

        var response = await _client.PostAsJsonAsync("/v1/auth/signup", request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var userId = body.GetProperty("data").GetProperty("userId").GetGuid();
        userId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task SignUp_WithDuplicateEmail_Returns409WithExpectedCode()
    {
        var sharedEmail = $"dup_{Guid.NewGuid():N}@example.com";

        var firstRequest = new
        {
            username = $"user_{Guid.NewGuid():N}"[..20],
            email = sharedEmail,
            password = "StrongPass1!"
        };
        await _client.PostAsJsonAsync("/v1/auth/signup", firstRequest);

        var secondRequest = new
        {
            username = $"user_{Guid.NewGuid():N}"[..20],
            email = sharedEmail,
            password = "StrongPass1!"
        };
        var response = await _client.PostAsJsonAsync("/v1/auth/signup", secondRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("code").GetString().Should().Be("user.email_conflict");
    }

    [Fact]
    public async Task SignUp_WithDuplicateUsername_Returns409WithExpectedCode()
    {
        var sharedUsername = $"usr{Guid.NewGuid():N}"[..20];

        var firstRequest = new
        {
            username = sharedUsername,
            email = $"first_{Guid.NewGuid():N}@example.com",
            password = "StrongPass1!"
        };
        await _client.PostAsJsonAsync("/v1/auth/signup", firstRequest);

        var secondRequest = new
        {
            username = sharedUsername,
            email = $"second_{Guid.NewGuid():N}@example.com",
            password = "StrongPass1!"
        };
        var response = await _client.PostAsJsonAsync("/v1/auth/signup", secondRequest);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("code").GetString().Should().Be("user.username_conflict");
    }

    [Theory]
    [InlineData("", "user@example.com", "StrongPass1!")]         // empty username
    [InlineData("ab", "user@example.com", "StrongPass1!")]        // username too short
    [InlineData("validuser", "", "StrongPass1!")]                  // empty email
    [InlineData("validuser", "not-an-email", "StrongPass1!")]      // invalid email format
    [InlineData("validuser", "user@example.com", "")]              // empty password
    [InlineData("validuser", "user@example.com", "short")]         // password too short
    public async Task SignUp_WithInvalidData_Returns400(string username, string email, string password)
    {
        var request = new { username, email, password };

        var response = await _client.PostAsJsonAsync("/v1/auth/signup", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
