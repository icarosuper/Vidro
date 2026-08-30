using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using VidroApi.IntegrationTests.Common;

namespace VidroApi.IntegrationTests.Auth;

/// <summary>
/// The shared <see cref="ApiFactory"/> runs with an effectively unlimited budget so the other
/// suites aren't throttled; this one boots its own host with a tiny budget instead.
/// </summary>
public class RateLimitedApiFactory : ApiFactory
{
    public const int PermitLimit = 3;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("RateLimit:AuthPermitLimit", PermitLimit.ToString());
        builder.UseSetting("RateLimit:AuthWindowSeconds", "60");
    }
}

public class RateLimitTests(RateLimitedApiFactory factory) : IClassFixture<RateLimitedApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task SignIn_BeyondThePermitLimit_Returns429()
    {
        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < RateLimitedApiFactory.PermitLimit + 2; attempt++)
        {
            var response = await _client.PostAsJsonAsync("/v1/auth/signin", new
            {
                email = "nobody@example.com",
                password = "WrongPass1!"
            });
            statuses.Add(response.StatusCode);
        }

        Assert.All(statuses.Take(RateLimitedApiFactory.PermitLimit),
            status => Assert.NotEqual(HttpStatusCode.TooManyRequests, status));
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    [Fact]
    public async Task SignUp_BeyondThePermitLimit_Returns429()
    {
        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < RateLimitedApiFactory.PermitLimit + 2; attempt++)
        {
            var response = await _client.PostAsJsonAsync("/v1/auth/signup", new
            {
                username = $"usr{Guid.NewGuid():N}"[..15],
                email = $"user_{Guid.NewGuid():N}@example.com",
                password = "StrongPass1!"
            });
            statuses.Add(response.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }
}
