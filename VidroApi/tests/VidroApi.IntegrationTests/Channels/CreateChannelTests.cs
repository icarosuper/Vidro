using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using VidroApi.IntegrationTests.Common;

namespace VidroApi.IntegrationTests.Channels;

public class CreateChannelTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Default)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task CreateChannel_WithValidData_Returns201WithHandle()
    {
        var (accessToken, _) = await SignUpAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.PostAsJsonAsync("/v1/channels", new
        {
            handle = "test-channel",
            name = "My Channel",
            description = "A great channel"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("data").GetProperty("handle").GetString().Should().Be("test-channel");
    }

    [Fact]
    public async Task CreateChannel_WithValidDataAndNoDescription_Returns201()
    {
        var (accessToken, _) = await SignUpAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.PostAsJsonAsync("/v1/channels", new
        {
            handle = "no-desc",
            name = "Channel Without Description"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateChannel_WithoutAccessToken_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/v1/channels", new
        {
            handle = "test-channel",
            name = "My Channel"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }


    [Fact]
    public async Task CreateChannel_WithEmptyHandle_Returns400()
    {
        var (accessToken, _) = await SignUpAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.PostAsJsonAsync("/v1/channels", new
        {
            handle = "",
            name = "My Channel"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateChannel_WithInvalidHandleCharacters_Returns400()
    {
        var (accessToken, _) = await SignUpAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.PostAsJsonAsync("/v1/channels", new
        {
            handle = "Invalid Handle!",
            name = "My Channel"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateChannel_WithDuplicateHandle_Returns409WithExpectedCode()
    {
        var (accessToken, _) = await SignUpAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        await _client.PostAsJsonAsync("/v1/channels", new { handle = "duplicate", name = "First" });
        var response = await _client.PostAsJsonAsync("/v1/channels", new { handle = "duplicate", name = "Second" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("code").GetString().Should().Be("channel.handle_already_in_use");
    }

    [Fact]
    public async Task CreateChannel_WithEmptyName_Returns400()
    {
        var (accessToken, _) = await SignUpAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.PostAsJsonAsync("/v1/channels", new
        {
            handle = "test-channel",
            name = ""
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateChannel_WithNameTooLong_Returns400()
    {
        var (accessToken, _) = await SignUpAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.PostAsJsonAsync("/v1/channels", new
        {
            handle = "test-channel",
            name = new string('a', 101)
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateChannel_WithDescriptionTooLong_Returns400()
    {
        var (accessToken, _) = await SignUpAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.PostAsJsonAsync("/v1/channels", new
        {
            handle = "test-channel",
            name = "My Channel",
            description = new string('a', 501)
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateChannel_WhenLimitReached_Returns409WithExpectedCode()
    {
        var (accessToken, _) = await SignUpAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        for (var i = 0; i < 10; i++)
        {
            await _client.PostAsJsonAsync("/v1/channels", new { handle = $"channel-{i}", name = $"Channel {i}" });
        }

        var response = await _client.PostAsJsonAsync("/v1/channels", new { handle = "one-too-many", name = "One Too Many" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        body.GetProperty("code").GetString().Should().Be("channel.limit_reached");
    }

    private async Task<(string AccessToken, string Username)> SignUpAndGetCredentials()
    {
        var username = $"usr{Guid.NewGuid():N}"[..15];
        var email = $"user_{Guid.NewGuid():N}@example.com";
        var password = "StrongPass1!";

        await _client.PostAsJsonAsync("/v1/auth/signup", new { username, email, password });

        var signInResponse = await _client.PostAsJsonAsync("/v1/auth/signin", new { email, password });
        var body = await signInResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var accessToken = body.GetProperty("data").GetProperty("accessToken").GetString()!;

        return (accessToken, username);
    }
}
