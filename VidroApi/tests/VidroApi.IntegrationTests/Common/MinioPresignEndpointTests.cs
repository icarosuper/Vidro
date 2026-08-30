using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Minio;
using VidroApi.Infrastructure;
using VidroApi.Infrastructure.Services;

namespace VidroApi.IntegrationTests.Common;

/// <summary>
/// Presigned URLs are opened by the browser, which may reach MinIO at a different host than
/// the API does. The signature covers the Host header, so the URL has to be signed for the
/// public endpoint — hence a second, dedicated client.
/// </summary>
public class MinioPresignEndpointTests
{
    private static ServiceProvider BuildProvider(string? publicEndpoint)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=x;Username=x;Password=x",
            ["ConnectionStrings:Redis"] = "localhost:6379",
            ["MinIO:Endpoint"] = "minio:9000",
            ["MinIO:AccessKey"] = "minioadmin",
            ["MinIO:SecretKey"] = "minioadmin",
            ["MinIO:BucketName"] = "videos",
            ["MinIO:UseSsl"] = "false",
            ["MinIO:PublicEndpoint"] = publicEndpoint
        };

        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection().AddInfrastructure(config).BuildServiceProvider();
    }

    [Fact]
    public void PresignClient_WhenPublicEndpointIsSet_UsesIt()
    {
        using var provider = BuildProvider("localhost:9000");

        var presignClient = provider.GetRequiredKeyedService<IMinioClient>(MinioService.PresignClientKey);

        presignClient.Config.Endpoint.Should().Be("http://localhost:9000");
    }

    [Fact]
    public void PresignClient_WhenPublicEndpointIsMissing_FallsBackToTheInternalEndpoint()
    {
        using var provider = BuildProvider(publicEndpoint: null);

        var presignClient = provider.GetRequiredKeyedService<IMinioClient>(MinioService.PresignClientKey);

        presignClient.Config.Endpoint.Should().Be("http://minio:9000");
    }
}
