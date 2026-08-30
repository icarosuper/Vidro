using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using VidroApi.IntegrationTests.Common;

namespace VidroApi.IntegrationTests.Channels;

public class UpdateChannelTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerOptions.Default)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task UpdateChannel_WithValidData_Returns204()
    {
        var (accessToken, _) = await SignUpAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        await CreateChannel("test-channel", "Original Name");

        var response = await _client.PutAsJsonAsync("/v1/channels/test-channel", new
        {
            name = "Updated Name",
            description = "Updated description"
        });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task UpdateChannel_ChangesArePersisted()
    {
        var (accessToken, ownerUsername) = await SignUpAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        await CreateChannel("test-channel", "Original Name");

        await _client.PutAsJsonAsync("/v1/channels/test-channel", new
        {
            name = "Updated Name",
            description = "Updated description"
        });

        _client.DefaultRequestHeaders.Authorization = null;
        var getResponse = await _client.GetAsync($"/v1/users/{ownerUsername}/channels/test-channel");
        var body = await getResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var data = body.GetProperty("data");

        data.GetProperty("name").GetString().Should().Be("Updated Name");
        data.GetProperty("description").GetString().Should().Be("Updated description");
    }

    [Fact]
    public async Task UpdateChannel_WithoutAuth_Returns401()
    {
        var (accessToken, _) = await SignUpAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        await CreateChannel("test-channel", "My Channel");

        _client.DefaultRequestHeaders.Authorization = null;
        var response = await _client.PutAsJsonAsync("/v1/channels/test-channel", new { name = "Updated" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateChannel_WhenNotOwner_Returns404()
    {
        var (ownerToken, _) = await SignUpAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        await CreateChannel("owner-channel", "Owner Channel");

        var (otherToken, _) = await SignUpAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);
        var response = await _client.PutAsJsonAsync("/v1/channels/owner-channel", new { name = "Hijacked" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateChannel_WithNonExistentHandle_Returns404()
    {
        var (accessToken, _) = await SignUpAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.PutAsJsonAsync("/v1/channels/nonexistent", new { name = "Updated" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateChannel_WithEmptyName_Returns400()
    {
        var (accessToken, _) = await SignUpAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        await CreateChannel("test-channel", "My Channel");

        var response = await _client.PutAsJsonAsync("/v1/channels/test-channel", new { name = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateChannel_WithNameTooLong_Returns400()
    {
        var (accessToken, _) = await SignUpAndGetCredentials();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        await CreateChannel("test-channel", "My Channel");

        var response = await _client.PutAsJsonAsync("/v1/channels/test-channel", new { name = new string('a', 101) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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

    private async Task CreateChannel(string handle, string name)
    {
        await _client.PostAsJsonAsync("/v1/channels", new { handle, name });
    }
}
