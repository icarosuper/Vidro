using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Minio;
using Npgsql;
using StackExchange.Redis;
using VidroApi.Application.Abstractions;
using VidroApi.Infrastructure.Persistence;
using VidroApi.Infrastructure.Services;
using VidroApi.Infrastructure.Settings;

namespace VidroApi.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration config)
    {
        // PostgreSQL. The data source is built here, and named, only because Npgsql tags every
        // metric it emits with that name. The default is the connection string minus the password
        // — so host, port, database and username would end up as a label on the unauthenticated
        // /metrics endpoint, and the label would change with every environment.
        var postgresDataSource = new NpgsqlDataSourceBuilder(config.GetConnectionString("Postgres"))
        {
            Name = "vidroapi"
        }.Build();
        services.AddSingleton(postgresDataSource);
        services.AddDbContext<AppDbContext>(opts => opts.UseNpgsql(postgresDataSource));

        // Redis
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(config.GetConnectionString("Redis")!));

        // MinIO
        var minioSettings = config.GetSection("MinIO").Get<MinioSettings>()!;
        services.AddMinio(client => client
            .WithEndpoint(minioSettings.Endpoint)
            .WithCredentials(minioSettings.AccessKey, minioSettings.SecretKey)
            .WithSSL(minioSettings.UseSsl));

        // Presigned URLs are consumed by the browser, which may reach MinIO at a different
        // host than this process does. The signature covers the Host header, so a URL signed
        // for the internal endpoint cannot just be string-rewritten — it needs its own client.
        var publicEndpoint = string.IsNullOrWhiteSpace(minioSettings.PublicEndpoint)
            ? minioSettings.Endpoint
            : minioSettings.PublicEndpoint;
        services.AddKeyedSingleton<IMinioClient>(MinioService.PresignClientKey, (_, _) =>
            new MinioClient()
                .WithEndpoint(publicEndpoint)
                .WithCredentials(minioSettings.AccessKey, minioSettings.SecretKey)
                .WithSSL(minioSettings.UseSsl)
                .Build());

        // Services
        services.AddScoped<IMinioService, MinioService>();
        services.AddScoped<IJobQueueService, RedisJobQueueService>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IDateTimeProvider, DateTimeProvider>();

        return services;
    }
}
