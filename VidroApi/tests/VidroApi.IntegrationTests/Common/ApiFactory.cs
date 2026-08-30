using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using VidroApi.Application.Abstractions;
using VidroApi.Infrastructure.Persistence;

#pragma warning disable CS0618

namespace VidroApi.IntegrationTests.Common;

public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("vidroapi_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
        builder.UseSetting("ConnectionStrings:Redis", "localhost:6379");
        builder.UseSetting("Jwt:Secret", "test-secret-that-is-long-enough-for-jwt-hmac-validation");
        builder.UseSetting("Jwt:AccessTokenExpiryMinutes", "15");
        builder.UseSetting("Jwt:RefreshTokenExpiryDays", "7");
        builder.UseSetting("MinIO:Endpoint", "localhost:9000");
        builder.UseSetting("MinIO:AccessKey", "test-access-key");
        builder.UseSetting("MinIO:SecretKey", "test-secret-key");
        builder.UseSetting("MinIO:BucketName", "test-bucket");
        builder.UseSetting("MinIO:UploadUrlTtlHours", "1");
        builder.UseSetting("RateLimit:AuthPermitLimit", "10000");
        builder.UseSetting("RateLimit:AuthWindowSeconds", "60");
        builder.UseSetting("ChannelSettings:MaxChannelsPerUser", "10");
        builder.UseSetting("VideoSettings:MaxTagsPerVideo", "10");
        builder.UseSetting("VideoSettings:ReconciliationIntervalMinutes", "60");
        builder.UseSetting("Webhook:Secret", "test-webhook-secret");
        builder.UseSetting("Webhook:MinioUploadToken", "test-minio-upload-token");
        builder.UseSetting("Api:BaseUrl", "http://localhost");
        builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost:3000");

        builder.ConfigureServices(services =>
        {
            var minioDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IMinioService));
            if (minioDescriptor is not null)
                services.Remove(minioDescriptor);

            var jobQueueDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IJobQueueService));
            if (jobQueueDescriptor is not null)
                services.Remove(jobQueueDescriptor);

            services.AddScoped<IMinioService, FakeMinioService>();
            services.AddScoped<IJobQueueService, FakeJobQueueService>();
        });
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await MigrateDatabase();
    }

    public new async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    private async Task MigrateDatabase()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }
}
