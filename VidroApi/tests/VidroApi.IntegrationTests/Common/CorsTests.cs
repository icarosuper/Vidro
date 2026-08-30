using FluentAssertions;

namespace VidroApi.IntegrationTests.Common;

/// <summary>
/// The front calls the API cross-origin from the browser. Without these headers every
/// client-side mutation is blocked before reaching an endpoint — and no endpoint test
/// would ever notice, since those bypass the browser.
/// </summary>
public class CorsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Preflight_FromAllowedOrigin_ReturnsAllowOriginHeader()
    {
        var response = await SendPreflight("http://localhost:3000");

        response.Headers.GetValues("Access-Control-Allow-Origin")
            .Should().Contain("http://localhost:3000");
    }

    [Fact]
    public async Task Preflight_FromUnknownOrigin_ReturnsNoCorsHeaders()
    {
        var response = await SendPreflight("http://evil.example");

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    private async Task<HttpResponseMessage> SendPreflight(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/v1/auth/signin");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        return await _client.SendAsync(request);
    }
}
