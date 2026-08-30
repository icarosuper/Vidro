# VideoApi Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build REST API video platform .NET 10, Clean Architecture + Vertical Slice, orchestrate VideoProcessor (Go) via Redis + MinIO.

**Architecture:** Clean Architecture 4 projects (Domain, Application, Infrastructure, Api). Each feature = single file with Request/Response/Validator/Handler/MapEndpoint. Dispatch via MediatR.

**Tech Stack:** .NET 10, PostgreSQL (EF Core + Npgsql), Redis (StackExchange.Redis), MinIO SDK, MediatR, FluentValidation, JWT Bearer, BCrypt.Net-Next, xUnit, Testcontainers

---

## Task 1: ✅ Scaffold da Solution

**Files:**
- Create: `src/VideoApi.Api/VideoApi.Api.csproj`
- Create: `src/VideoApi.Domain/VideoApi.Domain.csproj`
- Create: `src/VideoApi.Application/VideoApi.Application.csproj`
- Create: `src/VideoApi.Infrastructure/VideoApi.Infrastructure.csproj`
- Create: `tests/VideoApi.UnitTests/VideoApi.UnitTests.csproj`
- Create: `tests/VideoApi.IntegrationTests/VideoApi.IntegrationTests.csproj`
- Create: `VideoApi.sln`

**Step 1: Criar a solution e os projetos**

```bash
cd /home/icaro/Documentos/Projetos/VideoApi

dotnet new sln -n VideoApi

mkdir -p src tests

dotnet new webapi -n VideoApi.Api -o src/VideoApi.Api --use-minimal-apis
dotnet new classlib -n VideoApi.Domain -o src/VideoApi.Domain
dotnet new classlib -n VideoApi.Application -o src/VideoApi.Application
dotnet new classlib -n VideoApi.Infrastructure -o src/VideoApi.Infrastructure
dotnet new xunit -n VideoApi.UnitTests -o tests/VideoApi.UnitTests
dotnet new xunit -n VideoApi.IntegrationTests -o tests/VideoApi.IntegrationTests

dotnet sln add src/VideoApi.Api src/VideoApi.Domain src/VideoApi.Application src/VideoApi.Infrastructure tests/VideoApi.UnitTests tests/VideoApi.IntegrationTests
```

**Step 2: Configurar referências entre projetos**

```bash
# Application depende de Domain
dotnet add src/VideoApi.Application reference src/VideoApi.Domain

# Infrastructure depende de Application e Domain
dotnet add src/VideoApi.Infrastructure reference src/VideoApi.Application
dotnet add src/VideoApi.Infrastructure reference src/VideoApi.Domain

# Api depende de todos
dotnet add src/VideoApi.Api reference src/VideoApi.Application
dotnet add src/VideoApi.Api reference src/VideoApi.Infrastructure
dotnet add src/VideoApi.Api reference src/VideoApi.Domain

# Testes dependem de tudo
dotnet add tests/VideoApi.UnitTests reference src/VideoApi.Application
dotnet add tests/VideoApi.UnitTests reference src/VideoApi.Domain
dotnet add tests/VideoApi.IntegrationTests reference src/VideoApi.Api
dotnet add tests/VideoApi.IntegrationTests reference src/VideoApi.Infrastructure
```

**Step 3: Instalar NuGet packages**

```bash
# Application
dotnet add src/VideoApi.Application package MediatR
dotnet add src/VideoApi.Application package FluentValidation
dotnet add src/VideoApi.Application package Microsoft.Extensions.Options

# Infrastructure
dotnet add src/VideoApi.Infrastructure package Microsoft.EntityFrameworkCore
dotnet add src/VideoApi.Infrastructure package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/VideoApi.Infrastructure package Microsoft.EntityFrameworkCore.Design
dotnet add src/VideoApi.Infrastructure package StackExchange.Redis
dotnet add src/VideoApi.Infrastructure package Minio
dotnet add src/VideoApi.Infrastructure package BCrypt.Net-Next
dotnet add src/VideoApi.Infrastructure package Microsoft.Extensions.Options

# Api
dotnet add src/VideoApi.Api package MediatR.Extensions.Microsoft.DependencyInjection
dotnet add src/VideoApi.Api package FluentValidation.DependencyInjectionExtensions
dotnet add src/VideoApi.Api package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add src/VideoApi.Api package Microsoft.EntityFrameworkCore.Design

# Testes
dotnet add tests/VideoApi.UnitTests package FluentAssertions
dotnet add tests/VideoApi.UnitTests package NSubstitute
dotnet add tests/VideoApi.IntegrationTests package Microsoft.AspNetCore.Mvc.Testing
dotnet add tests/VideoApi.IntegrationTests package Testcontainers.PostgreSql
dotnet add tests/VideoApi.IntegrationTests package Testcontainers.Redis
dotnet add tests/VideoApi.IntegrationTests package FluentAssertions
```

**Step 4: Remover arquivos boilerplate desnecessários**

```bash
rm src/VideoApi.Api/Controllers -rf 2>/dev/null || true
rm src/VideoApi.Domain/Class1.cs
rm src/VideoApi.Application/Class1.cs
rm src/VideoApi.Infrastructure/Class1.cs
```

**Step 5: Verificar que compila**

```bash
dotnet build
```
Expected: `Build succeeded.`

**Step 6: Commit**

```bash
git init
echo -e "bin/\nobj/\n*.user\n.env\nappsettings*.local.json" > .gitignore
git add .
git commit -m "chore: scaffold solution with 4 projects and test projects"
```

---

## Task 2: ✅ Domain — Entidades e Enums

**Files:**
- Create: `src/VideoApi.Domain/Entities/User.cs`
- Create: `src/VideoApi.Domain/Entities/Channel.cs`
- Create: `src/VideoApi.Domain/Entities/ChannelFollower.cs`
- Create: `src/VideoApi.Domain/Entities/Video.cs`
- Create: `src/VideoApi.Domain/Entities/VideoArtifacts.cs`
- Create: `src/VideoApi.Domain/Entities/VideoMetadata.cs`
- Create: `src/VideoApi.Domain/Entities/Reaction.cs`
- Create: `src/VideoApi.Domain/Entities/Comment.cs`
- Create: `src/VideoApi.Domain/Entities/RefreshToken.cs`
- Create: `src/VideoApi.Domain/Enums/VideoStatus.cs`
- Create: `src/VideoApi.Domain/Enums/ReactionType.cs`
- Create: `src/VideoApi.Domain/Errors/DomainError.cs`

**Step 1: Criar enums**

```csharp
// src/VideoApi.Domain/Enums/VideoStatus.cs
namespace VideoApi.Domain.Enums;

public enum VideoStatus
{
    PendingUpload,
    Processing,
    Ready,
    Failed
}
```

```csharp
// src/VideoApi.Domain/Enums/ReactionType.cs
namespace VideoApi.Domain.Enums;

public enum ReactionType { Like, Dislike }
```

**Step 2: Criar entidades**

```csharp
// src/VideoApi.Domain/Entities/User.cs
namespace VideoApi.Domain.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string PasswordHash { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<Channel> Channels { get; set; } = [];
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}
```

```csharp
// src/VideoApi.Domain/Entities/Channel.cs
namespace VideoApi.Domain.Entities;

public class Channel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public string? AvatarPath { get; set; }
    public long FollowerCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public User User { get; set; } = default!;
    public ICollection<Video> Videos { get; set; } = [];
    public ICollection<ChannelFollower> Followers { get; set; } = [];
}
```

```csharp
// src/VideoApi.Domain/Entities/ChannelFollower.cs
namespace VideoApi.Domain.Entities;

public class ChannelFollower
{
    public Guid ChannelId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Channel Channel { get; set; } = default!;
    public User User { get; set; } = default!;
}
```

```csharp
// src/VideoApi.Domain/Entities/Video.cs
using VideoApi.Domain.Enums;

namespace VideoApi.Domain.Entities;

public class Video
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChannelId { get; set; }
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
    public VideoStatus Status { get; set; } = VideoStatus.PendingUpload;
    public string[] Tags { get; set; } = [];
    public long LikeCount { get; set; }
    public long DislikeCount { get; set; }
    public long ViewCount { get; set; }
    public double? DurationSeconds { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Channel Channel { get; set; } = default!;
    public VideoArtifacts? Artifacts { get; set; }
    public VideoMetadata? Metadata { get; set; }
    public ICollection<Reaction> Reactions { get; set; } = [];
    public ICollection<Comment> Comments { get; set; } = [];
}
```

```csharp
// src/VideoApi.Domain/Entities/VideoArtifacts.cs
namespace VideoApi.Domain.Entities;

public class VideoArtifacts
{
    public Guid VideoId { get; set; }
    public string? VideoPath { get; set; }
    public string? ThumbnailsPath { get; set; }
    public string? AudioPath { get; set; }
    public string? PreviewPath { get; set; }
    public string? HlsPath { get; set; }

    public Video Video { get; set; } = default!;
}
```

```csharp
// src/VideoApi.Domain/Entities/VideoMetadata.cs
namespace VideoApi.Domain.Entities;

public class VideoMetadata
{
    public Guid VideoId { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double Fps { get; set; }
    public string? VideoCodec { get; set; }
    public string? AudioCodec { get; set; }
    public long Bitrate { get; set; }
    public long SizeBytes { get; set; }

    public Video Video { get; set; } = default!;
}
```

```csharp
// src/VideoApi.Domain/Entities/Reaction.cs
using VideoApi.Domain.Enums;

namespace VideoApi.Domain.Entities;

public class Reaction
{
    public Guid UserId { get; set; }
    public Guid VideoId { get; set; }
    public ReactionType Type { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public User User { get; set; } = default!;
    public Video Video { get; set; } = default!;
}
```

```csharp
// src/VideoApi.Domain/Entities/Comment.cs
namespace VideoApi.Domain.Entities;

public class Comment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VideoId { get; set; }
    public Guid UserId { get; set; }
    public string Content { get; set; } = default!;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Video Video { get; set; } = default!;
    public User User { get; set; } = default!;
}
```

```csharp
// src/VideoApi.Domain/Entities/RefreshToken.cs
namespace VideoApi.Domain.Entities;

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Token { get; set; } = default!;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsRevoked { get; set; }

    public User User { get; set; } = default!;
}
```

**Step 3: Criar DomainError (result type, no exceptions)**

```csharp
// src/VideoApi.Domain/Errors/DomainError.cs
namespace VideoApi.Domain.Errors;

public record DomainError(string Code, string Message)
{
    public static DomainError NotFound(string entity) =>
        new("NOT_FOUND", $"{entity} não encontrado.");

    public static DomainError Unauthorized() =>
        new("UNAUTHORIZED", "Acesso não autorizado.");

    public static DomainError Conflict(string message) =>
        new("CONFLICT", message);

    public static DomainError Validation(string message) =>
        new("VALIDATION", message);
}
```

**Step 4: Verificar que compila**

```bash
dotnet build src/VideoApi.Domain
```
Expected: `Build succeeded.`

**Step 5: Commit**

```bash
git add src/VideoApi.Domain
git commit -m "feat: add domain entities and enums"
```

---

## Task 3: ✅ Infrastructure — Settings e Interfaces

**Files:**
- Create: `src/VideoApi.Application/Interfaces/IMinioService.cs`
- Create: `src/VideoApi.Application/Interfaces/IJobQueueService.cs`
- Create: `src/VideoApi.Infrastructure/Settings/JwtSettings.cs`
- Create: `src/VideoApi.Infrastructure/Settings/MinioSettings.cs`
- Create: `src/VideoApi.Infrastructure/Settings/VideoSettings.cs`
- Create: `src/VideoApi.Infrastructure/Settings/TrendingSettings.cs`
- Create: `src/VideoApi.Infrastructure/Settings/WebhookSettings.cs`

**Step 1: Criar interfaces na camada Application**

```csharp
// src/VideoApi.Application/Interfaces/IMinioService.cs
namespace VideoApi.Application.Interfaces;

public interface IMinioService
{
    Task<(string Url, DateTimeOffset ExpiresAt)> GenerateUploadUrlAsync(
        string objectKey, TimeSpan ttl, CancellationToken ct = default);

    Task<string> GenerateDownloadUrlAsync(
        string objectKey, TimeSpan ttl, CancellationToken ct = default);

    Task<bool> ObjectExistsAsync(string objectKey, CancellationToken ct = default);
}
```

```csharp
// src/VideoApi.Application/Interfaces/IJobQueueService.cs
namespace VideoApi.Application.Interfaces;

public interface IJobQueueService
{
    Task PublishJobAsync(string videoId, string callbackUrl, CancellationToken ct = default);
}
```

**Step 2: Criar classes de settings**

```csharp
// src/VideoApi.Infrastructure/Settings/JwtSettings.cs
namespace VideoApi.Infrastructure.Settings;

public class JwtSettings
{
    public string Secret { get; set; } = default!;
    public int AccessTokenExpiryMinutes { get; set; } = 15;
    public int RefreshTokenExpiryDays { get; set; } = 7;
}
```

```csharp
// src/VideoApi.Infrastructure/Settings/MinioSettings.cs
namespace VideoApi.Infrastructure.Settings;

public class MinioSettings
{
    public string Endpoint { get; set; } = default!;
    public string AccessKey { get; set; } = default!;
    public string SecretKey { get; set; } = default!;
    public string BucketName { get; set; } = default!;
    public bool UseSSL { get; set; }
    public int UploadUrlTtlHours { get; set; } = 2;
}
```

```csharp
// src/VideoApi.Infrastructure/Settings/VideoSettings.cs
namespace VideoApi.Infrastructure.Settings;

public class VideoSettings
{
    public int MaxTagsPerVideo { get; set; } = 10;
}
```

```csharp
// src/VideoApi.Infrastructure/Settings/TrendingSettings.cs
namespace VideoApi.Infrastructure.Settings;

public class TrendingSettings
{
    public double ViewCountWeight { get; set; } = 1.0;
    public double LikeCountWeight { get; set; } = 2.0;
    public double TimeDecayFactor { get; set; } = 1.5;
    public int WindowHours { get; set; } = 48;
}
```

```csharp
// src/VideoApi.Infrastructure/Settings/WebhookSettings.cs
namespace VideoApi.Infrastructure.Settings;

public class WebhookSettings
{
    public string Secret { get; set; } = string.Empty;
}
```

**Step 3: Verificar que compila**

```bash
dotnet build src/VideoApi.Application src/VideoApi.Infrastructure
```
Expected: `Build succeeded.`

**Step 4: Commit**

```bash
git add src/VideoApi.Application src/VideoApi.Infrastructure
git commit -m "feat: add application interfaces and infrastructure settings"
```

---

## Task 4: ✅ Infrastructure — EF Core DbContext

**Files:**
- Create: `src/VideoApi.Infrastructure/Persistence/AppDbContext.cs`
- Create: `src/VideoApi.Infrastructure/Persistence/Configurations/UserConfiguration.cs`
- Create: `src/VideoApi.Infrastructure/Persistence/Configurations/ChannelConfiguration.cs`
- Create: `src/VideoApi.Infrastructure/Persistence/Configurations/VideoConfiguration.cs`
- Create: `src/VideoApi.Infrastructure/Persistence/Configurations/ReactionConfiguration.cs`
- Create: `src/VideoApi.Infrastructure/Persistence/Configurations/CommentConfiguration.cs`

**Step 1: Criar o DbContext**

```csharp
// src/VideoApi.Infrastructure/Persistence/AppDbContext.cs
using Microsoft.EntityFrameworkCore;
using VideoApi.Domain.Entities;

namespace VideoApi.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<ChannelFollower> ChannelFollowers => Set<ChannelFollower>();
    public DbSet<Video> Videos => Set<Video>();
    public DbSet<VideoArtifacts> VideoArtifacts => Set<VideoArtifacts>();
    public DbSet<VideoMetadata> VideoMetadata => Set<VideoMetadata>();
    public DbSet<Reaction> Reactions => Set<Reaction>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
```

**Step 2: Criar configurações de entidades**

```csharp
// src/VideoApi.Infrastructure/Persistence/Configurations/UserConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoApi.Domain.Entities;

namespace VideoApi.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);
        builder.HasIndex(u => u.Email).IsUnique();
        builder.HasIndex(u => u.Username).IsUnique();
        builder.Property(u => u.Email).IsRequired();
        builder.Property(u => u.Username).IsRequired();
        builder.Property(u => u.PasswordHash).IsRequired();
    }
}
```

```csharp
// src/VideoApi.Infrastructure/Persistence/Configurations/ChannelConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoApi.Domain.Entities;

namespace VideoApi.Infrastructure.Persistence.Configurations;

public class ChannelConfiguration : IEntityTypeConfiguration<Channel>
{
    public void Configure(EntityTypeBuilder<Channel> builder)
    {
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => c.UserId);
        builder.Property(c => c.Name).IsRequired();

        builder.HasMany(c => c.Followers)
               .WithOne(f => f.Channel)
               .HasForeignKey(f => f.ChannelId);

        // ChannelFollower PK composta
        builder.HasMany(c => c.Videos)
               .WithOne(v => v.Channel)
               .HasForeignKey(v => v.ChannelId);
    }
}

public class ChannelFollowerConfiguration : IEntityTypeConfiguration<ChannelFollower>
{
    public void Configure(EntityTypeBuilder<ChannelFollower> builder)
    {
        builder.HasKey(f => new { f.ChannelId, f.UserId });
        builder.HasIndex(f => new { f.UserId, f.ChannelId });
    }
}
```

```csharp
// src/VideoApi.Infrastructure/Persistence/Configurations/VideoConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoApi.Domain.Entities;
using VideoApi.Domain.Enums;

namespace VideoApi.Infrastructure.Persistence.Configurations;

public class VideoConfiguration : IEntityTypeConfiguration<Video>
{
    public void Configure(EntityTypeBuilder<Video> builder)
    {
        builder.HasKey(v => v.Id);
        builder.HasIndex(v => new { v.ChannelId, v.CreatedAt });
        builder.HasIndex(v => new { v.Status, v.CreatedAt });
        builder.HasIndex(v => v.CreatedAt);

        builder.Property(v => v.Status)
               .HasConversion<string>();

        builder.Property(v => v.Tags)
               .HasColumnType("text[]");

        builder.HasIndex(v => v.Tags)
               .HasMethod("gin");

        builder.HasOne(v => v.Artifacts)
               .WithOne(a => a.Video)
               .HasForeignKey<VideoArtifacts>(a => a.VideoId);

        builder.HasOne(v => v.Metadata)
               .WithOne(m => m.Video)
               .HasForeignKey<VideoMetadata>(m => m.VideoId);
    }
}
```

```csharp
// src/VideoApi.Infrastructure/Persistence/Configurations/ReactionConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoApi.Domain.Entities;

namespace VideoApi.Infrastructure.Persistence.Configurations;

public class ReactionConfiguration : IEntityTypeConfiguration<Reaction>
{
    public void Configure(EntityTypeBuilder<Reaction> builder)
    {
        builder.HasKey(r => new { r.UserId, r.VideoId });
        builder.Property(r => r.Type).HasConversion<string>();
    }
}
```

```csharp
// src/VideoApi.Infrastructure/Persistence/Configurations/CommentConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VideoApi.Domain.Entities;

namespace VideoApi.Infrastructure.Persistence.Configurations;

public class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => new { c.VideoId, c.CreatedAt });
        builder.Property(c => c.Content).IsRequired();
    }
}
```

**Step 3: Compilar**

```bash
dotnet build src/VideoApi.Infrastructure
```
Expected: `Build succeeded.`

**Step 4: Commit**

```bash
git add src/VideoApi.Infrastructure
git commit -m "feat: add EF Core DbContext and entity configurations"
```

---

## Task 5: ✅ Infrastructure — Implementações de Serviços

**Files:**
- Create: `src/VideoApi.Infrastructure/Services/MinioService.cs`
- Create: `src/VideoApi.Infrastructure/Services/RedisJobQueueService.cs`
- Create: `src/VideoApi.Infrastructure/DependencyInjection.cs`

**Step 1: Implementar MinioService**

```csharp
// src/VideoApi.Infrastructure/Services/MinioService.cs
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using VideoApi.Application.Interfaces;
using VideoApi.Infrastructure.Settings;

namespace VideoApi.Infrastructure.Services;

public class MinioService(IMinioClient minioClient, IOptions<MinioSettings> options)
    : IMinioService
{
    private readonly MinioSettings _settings = options.Value;

    public async Task<(string Url, DateTimeOffset ExpiresAt)> GenerateUploadUrlAsync(
        string objectKey, TimeSpan ttl, CancellationToken ct = default)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(ttl);
        var url = await minioClient.PresignedPutObjectAsync(
            new PresignedPutObjectArgs()
                .WithBucket(_settings.BucketName)
                .WithObject(objectKey)
                .WithExpiry((int)ttl.TotalSeconds));
        return (url, expiresAt);
    }

    public async Task<string> GenerateDownloadUrlAsync(
        string objectKey, TimeSpan ttl, CancellationToken ct = default)
    {
        return await minioClient.PresignedGetObjectAsync(
            new PresignedGetObjectArgs()
                .WithBucket(_settings.BucketName)
                .WithObject(objectKey)
                .WithExpiry((int)ttl.TotalSeconds));
    }

    public async Task<bool> ObjectExistsAsync(string objectKey, CancellationToken ct = default)
    {
        try
        {
            await minioClient.StatObjectAsync(
                new StatObjectArgs()
                    .WithBucket(_settings.BucketName)
                    .WithObject(objectKey), ct);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
```

**Step 2: Implementar RedisJobQueueService**

```csharp
// src/VideoApi.Infrastructure/Services/RedisJobQueueService.cs
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;
using VideoApi.Application.Interfaces;

namespace VideoApi.Infrastructure.Services;

public class RedisJobQueueService(IConnectionMultiplexer redis, IConfiguration config)
    : IJobQueueService
{
    private readonly string _queueName =
        config["Redis:ProcessingRequestQueue"] ?? "video_queue";

    public async Task PublishJobAsync(string videoId, string callbackUrl, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();

        // Grava estado inicial no Redis (TTL 24h) — espelha o que o VideoProcessor espera
        var jobState = new
        {
            status = "pending",
            callback_url = callbackUrl,
            retry_count = 0,
            created_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            updated_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        await db.StringSetAsync(
            $"job:{videoId}",
            JsonSerializer.Serialize(jobState),
            TimeSpan.FromHours(24));

        await db.ListLeftPushAsync(_queueName, videoId);
    }
}
```

**Step 3: Criar extensão de DI**

```csharp
// src/VideoApi.Infrastructure/DependencyInjection.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Minio;
using StackExchange.Redis;
using VideoApi.Application.Interfaces;
using VideoApi.Infrastructure.Persistence;
using VideoApi.Infrastructure.Services;
using VideoApi.Infrastructure.Settings;

namespace VideoApi.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration config)
    {
        // PostgreSQL
        services.AddDbContext<AppDbContext>(opts =>
            opts.UseNpgsql(config.GetConnectionString("Postgres")));

        // Redis
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(config.GetConnectionString("Redis")!));

        // MinIO
        services.Configure<MinioSettings>(config.GetSection("MinIO"));
        var minioSettings = config.GetSection("MinIO").Get<MinioSettings>()!;
        services.AddMinio(client => client
            .WithEndpoint(minioSettings.Endpoint)
            .WithCredentials(minioSettings.AccessKey, minioSettings.SecretKey)
            .WithSSL(minioSettings.UseSSL));

        // Settings
        services.Configure<JwtSettings>(config.GetSection("Jwt"));
        services.Configure<VideoSettings>(config.GetSection("VideoSettings"));
        services.Configure<TrendingSettings>(config.GetSection("TrendingSettings"));
        services.Configure<WebhookSettings>(config.GetSection("Webhook"));

        // Services
        services.AddScoped<IMinioService, MinioService>();
        services.AddScoped<IJobQueueService, RedisJobQueueService>();

        return services;
    }
}
```

**Step 4: Compilar**

```bash
dotnet build src/VideoApi.Infrastructure
```
Expected: `Build succeeded.`

**Step 5: Commit**

```bash
git add src/VideoApi.Infrastructure
git commit -m "feat: add MinIO and Redis service implementations"
```

---

## Task 6: ✅ Api — Program.cs e configuração base

**Files:**
- Modify: `src/VideoApi.Api/Program.cs`
- Create: `src/VideoApi.Api/appsettings.json`
- Create: `src/VideoApi.Api/appsettings.Development.json`
- Create: `src/VideoApi.Api/Middleware/ExceptionMiddleware.cs`
- Create: `src/VideoApi.Application/DependencyInjection.cs`
- Create: `src/VideoApi.Application/Common/PagedResult.cs`

**Step 1: Criar PagedResult (usado em todas as listagens)**

```csharp
// src/VideoApi.Application/Common/PagedResult.cs
namespace VideoApi.Application.Common;

public record PagedResult<T>(IReadOnlyList<T> Data, string? NextCursor);
```

**Step 2: Criar extensão de DI da camada Application**

```csharp
// src/VideoApi.Application/DependencyInjection.cs
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace VideoApi.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        // Pipeline behavior para validação automática
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        return services;
    }
}
```

**Step 3: Criar ValidationBehavior**

```csharp
// src/VideoApi.Application/Common/ValidationBehavior.cs
using FluentValidation;
using MediatR;

namespace VideoApi.Application.Common;

public class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (!validators.Any()) return await next();

        var context = new ValidationContext<TRequest>(request);
        var failures = validators
            .Select(v => v.Validate(context))
            .SelectMany(r => r.Errors)
            .Where(f => f != null)
            .ToList();

        if (failures.Count != 0)
            throw new ValidationException(failures);

        return await next();
    }
}
```

**Step 4: Criar ExceptionMiddleware**

```csharp
// src/VideoApi.Api/Middleware/ExceptionMiddleware.cs
using FluentValidation;
using System.Text.Json;

namespace VideoApi.Api.Middleware;

public class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await next(ctx);
        }
        catch (ValidationException ex)
        {
            ctx.Response.StatusCode = 400;
            ctx.Response.ContentType = "application/json";
            var errors = ex.Errors.Select(e => new { e.PropertyName, e.ErrorMessage });
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { errors }));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception");
            ctx.Response.StatusCode = 500;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { error = "Internal server error" }));
        }
    }
}
```

**Step 5: Configurar Program.cs**

```csharp
// src/VideoApi.Api/Program.cs
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using VideoApi.Api.Middleware;
using VideoApi.Application;
using VideoApi.Infrastructure;
using VideoApi.Infrastructure.Settings;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseMiddleware<ExceptionMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Cada slice registra seu endpoint aqui — será preenchido nas próximas tasks
// Example: CreateVideo.MapEndpoint(app);

app.Run();

public partial class Program { } // necessário para integration tests
```

**Step 6: Configurar appsettings**

```json
// src/VideoApi.Api/appsettings.json
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Database=videoapi;Username=postgres;Password=postgres",
    "Redis": "localhost:6379"
  },
  "MinIO": {
    "Endpoint": "localhost:9000",
    "AccessKey": "minioadmin",
    "SecretKey": "minioadmin",
    "BucketName": "videos",
    "UseSSL": false,
    "UploadUrlTtlHours": 2
  },
  "Jwt": {
    "Secret": "your-super-secret-key-change-in-production-minimum-32-chars",
    "AccessTokenExpiryMinutes": 15,
    "RefreshTokenExpiryDays": 7
  },
  "Redis": {
    "ProcessingRequestQueue": "video_queue"
  },
  "VideoSettings": {
    "MaxTagsPerVideo": 10
  },
  "TrendingSettings": {
    "ViewCountWeight": 1.0,
    "LikeCountWeight": 2.0,
    "TimeDecayFactor": 1.5,
    "WindowHours": 48
  },
  "Webhook": {
    "Secret": ""
  },
  "Logging": {
    "LogLevel": { "Default": "Information" }
  }
}
```

**Step 7: Compilar**

```bash
dotnet build
```
Expected: `Build succeeded.`

**Step 8: Commit**

```bash
git add .
git commit -m "feat: configure Program.cs, DI, JWT auth and exception middleware"
```

---

## Task 7: ✅ Migrations e docker-compose

**Files:**
- Create: `docker-compose.yml`
- Create: `src/VideoApi.Infrastructure/Persistence/Migrations/` (gerado)

**Step 1: Criar docker-compose**

```yaml
# docker-compose.yml
services:
  postgres:
    image: postgres:16
    environment:
      POSTGRES_DB: videoapi
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
    ports:
      - "5432:5432"
    volumes:
      - postgres_data:/var/lib/postgresql/data

  redis:
    image: redis:7-alpine
    ports:
      - "6379:6379"

  minio:
    image: minio/minio
    command: server /data --console-address ":9001"
    environment:
      MINIO_ROOT_USER: minioadmin
      MINIO_ROOT_PASSWORD: minioadmin
    ports:
      - "9000:9000"
      - "9001:9001"
    volumes:
      - minio_data:/data

volumes:
  postgres_data:
  minio_data:
```

**Step 2: Subir dependências**

```bash
docker-compose up -d postgres redis minio
```
Expected: containers `postgres`, `redis`, `minio` running.

**Step 3: Criar migration inicial**

```bash
dotnet ef migrations add InitialCreate \
  --project src/VideoApi.Infrastructure \
  --startup-project src/VideoApi.Api \
  --output-dir Persistence/Migrations
```
Expected: migration files criados em `Persistence/Migrations/`.

**Step 4: Aplicar migration**

```bash
dotnet ef database update \
  --project src/VideoApi.Infrastructure \
  --startup-project src/VideoApi.Api
```
Expected: `Done.`

**Step 5: Commit**

```bash
git add .
git commit -m "feat: add docker-compose and initial EF Core migration"
```

---

## Task 8: ✅ Auth — Register

**Files:**
- Create: `src/VideoApi.Application/Auth/Register.cs`
- Create: `tests/VideoApi.UnitTests/Auth/RegisterTests.cs`

**Step 1: Escrever o teste**

```csharp
// tests/VideoApi.UnitTests/Auth/RegisterTests.cs
using FluentAssertions;
using NSubstitute;
using VideoApi.Application.Auth;
using VideoApi.Infrastructure.Persistence;
// ... setup com InMemory ou mock do DbContext
// Teste: registrar usuário novo → retorna userId
// Teste: registrar email duplicado → lança ValidationException
```

> Nota: unit tests de handlers com DbContext — use `UseInMemoryDatabase` do EF Core ou mock com NSubstitute.

**Step 2: Rodar o teste para ver falhar**

```bash
dotnet test tests/VideoApi.UnitTests --filter "FullyQualifiedName~RegisterTests"
```
Expected: FAIL — `Register` não existe.

**Step 3: Implementar o slice**

```csharp
// src/VideoApi.Application/Auth/Register.cs
using BCrypt.Net;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using VideoApi.Domain.Entities;
using VideoApi.Infrastructure.Persistence;

namespace VideoApi.Application.Auth;

public static class Register
{
    public record Request(string Username, string Email, string Password) : IRequest<Response>;
    public record Response(Guid UserId);

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Username).NotEmpty().MinimumLength(3).MaximumLength(50);
            RuleFor(x => x.Email).NotEmpty().EmailAddress();
            RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
        }
    }

    public class Handler(AppDbContext db) : IRequestHandler<Request, Response>
    {
        public async Task<Response> Handle(Request req, CancellationToken ct)
        {
            var exists = await db.Users.AnyAsync(
                u => u.Email == req.Email || u.Username == req.Username, ct);

            if (exists)
                throw new ValidationException("Email ou username já em uso.");

            var user = new User
            {
                Username = req.Username,
                Email = req.Email.ToLowerInvariant(),
                PasswordHash = BCrypt.HashPassword(req.Password)
            };

            db.Users.Add(user);
            await db.SaveChangesAsync(ct);

            return new Response(user.Id);
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/auth/register", async (Request req, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(req, ct);
            return Results.Created($"/users/{result.UserId}", result);
        });
}
```

**Step 4: Registrar endpoint no Program.cs**

Adicionar em `Program.cs` antes de `app.Run()`:
```csharp
Register.MapEndpoint(app);
```

**Step 5: Rodar os testes**

```bash
dotnet test tests/VideoApi.UnitTests --filter "FullyQualifiedName~RegisterTests"
```
Expected: PASS.

**Step 6: Commit**

```bash
git add .
git commit -m "feat: add Auth/Register slice"
```

---

## Task 9: ✅ Auth — Login e RefreshToken

**Files:**
- Create: `src/VideoApi.Application/Auth/Login.cs`
- Create: `src/VideoApi.Application/Auth/RefreshToken.cs`
- Create: `src/VideoApi.Infrastructure/Services/TokenService.cs`

**Step 1: Criar TokenService**

```csharp
// src/VideoApi.Infrastructure/Services/TokenService.cs
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using VideoApi.Domain.Entities;
using VideoApi.Infrastructure.Settings;

namespace VideoApi.Infrastructure.Services;

public class TokenService(IOptions<JwtSettings> options)
{
    private readonly JwtSettings _settings = options.Value;

    public string GenerateAccessToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.Username)
        };

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_settings.AccessTokenExpiryMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public (string Token, DateTimeOffset ExpiresAt) GenerateRefreshToken()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var expiresAt = DateTimeOffset.UtcNow.AddDays(_settings.RefreshTokenExpiryDays);
        return (token, expiresAt);
    }
}
```

**Step 2: Registrar TokenService no DI**

Em `src/VideoApi.Infrastructure/DependencyInjection.cs`, adicionar:
```csharp
services.AddScoped<TokenService>();
```

**Step 3: Implementar Login**

```csharp
// src/VideoApi.Application/Auth/Login.cs
using BCrypt.Net;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using VideoApi.Domain.Entities;
using VideoApi.Infrastructure.Persistence;
using VideoApi.Infrastructure.Services;

namespace VideoApi.Application.Auth;

public static class Login
{
    public record Request(string Email, string Password) : IRequest<Response>;
    public record Response(string AccessToken, string RefreshToken);

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Email).NotEmpty().EmailAddress();
            RuleFor(x => x.Password).NotEmpty();
        }
    }

    public class Handler(AppDbContext db, TokenService tokenService)
        : IRequestHandler<Request, Response>
    {
        public async Task<Response> Handle(Request req, CancellationToken ct)
        {
            var user = await db.Users
                .FirstOrDefaultAsync(u => u.Email == req.Email.ToLowerInvariant(), ct);

            if (user is null || !BCrypt.Verify(req.Password, user.PasswordHash))
                throw new ValidationException("Credenciais inválidas.");

            var accessToken = tokenService.GenerateAccessToken(user);
            var (refreshTokenValue, expiresAt) = tokenService.GenerateRefreshToken();

            db.RefreshTokens.Add(new RefreshToken
            {
                UserId = user.Id,
                Token = refreshTokenValue,
                ExpiresAt = expiresAt
            });
            await db.SaveChangesAsync(ct);

            return new Response(accessToken, refreshTokenValue);
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/auth/login", async (Request req, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(req, ct)));
}
```

**Step 4: Implementar RefreshToken**

```csharp
// src/VideoApi.Application/Auth/RefreshToken.cs
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using VideoApi.Infrastructure.Persistence;
using VideoApi.Infrastructure.Services;

namespace VideoApi.Application.Auth;

public static class RefreshToken
{
    public record Request(string Token) : IRequest<Response>;
    public record Response(string AccessToken, string RefreshToken);

    public class Validator : AbstractValidator<Request>
    {
        public Validator() => RuleFor(x => x.Token).NotEmpty();
    }

    public class Handler(AppDbContext db, TokenService tokenService)
        : IRequestHandler<Request, Response>
    {
        public async Task<Response> Handle(Request req, CancellationToken ct)
        {
            var existing = await db.RefreshTokens
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.Token == req.Token && !t.IsRevoked, ct);

            if (existing is null || existing.ExpiresAt < DateTimeOffset.UtcNow)
                throw new ValidationException("Refresh token inválido ou expirado.");

            // Rotacionar: revogar o atual e emitir novo
            existing.IsRevoked = true;

            var accessToken = tokenService.GenerateAccessToken(existing.User);
            var (newToken, expiresAt) = tokenService.GenerateRefreshToken();

            db.RefreshTokens.Add(new Domain.Entities.RefreshToken
            {
                UserId = existing.UserId,
                Token = newToken,
                ExpiresAt = expiresAt
            });

            await db.SaveChangesAsync(ct);
            return new Response(accessToken, newToken);
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/auth/refresh", async (Request req, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(req, ct)));
}
```

**Step 5: Registrar endpoints no Program.cs**

```csharp
Login.MapEndpoint(app);
RefreshToken.MapEndpoint(app);
```

**Step 6: Compilar e testar**

```bash
dotnet build && dotnet test tests/VideoApi.UnitTests
```
Expected: `Build succeeded.` / testes passando.

**Step 7: Commit**

```bash
git add .
git commit -m "feat: add Auth/Login and Auth/RefreshToken slices"
```

---

## Task 9.1: ✅ Auth — SignOut

**Descrição:** Logout — revoga refresh token do usuário.

**Implementado em:** `src/VidroApi.Api/Features/Auth/SignOut.cs`

- Endpoint `POST /v1/auth/signout` — requer autenticação
- Input: `RefreshToken` (body)
- Remove refresh token do banco (soft-delete ou remoção)
- Impede reutilização
- Validação: refresh token deve pertencer ao usuário autenticado

---

## Task 10: ✅ Channels — CRUD

**Files:**
- Create: `src/VideoApi.Application/Channels/CreateChannel.cs`
- Create: `src/VideoApi.Application/Channels/GetChannel.cs`
- Create: `src/VideoApi.Application/Channels/UpdateChannel.cs`
- Create: `src/VideoApi.Application/Channels/DeleteChannel.cs`

**Padrão para extrair UserId do JWT** (usar em todos os handlers autenticados):

```csharp
// Helper — adicionar em Application/Common/ClaimsPrincipalExtensions.cs
using System.Security.Claims;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
```

**Step 1: Implementar CreateChannel**

```csharp
// src/VideoApi.Application/Channels/CreateChannel.cs
using FluentValidation;
using MediatR;
using VideoApi.Application.Common;
using VideoApi.Domain.Entities;
using VideoApi.Infrastructure.Persistence;

namespace VideoApi.Application.Channels;

public static class CreateChannel
{
    public record Request(string Name, string? Description) : IRequest<Response>;
    public record Response(Guid ChannelId);

    public class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
            RuleFor(x => x.Description).MaximumLength(500);
        }
    }

    public class Handler(AppDbContext db) : IRequestHandler<Request, Response>
    {
        // userId injetado via HttpContext — ver MapEndpoint
        public Guid UserId { get; set; }

        public async Task<Response> Handle(Request req, CancellationToken ct)
        {
            var channel = new Channel
            {
                UserId = UserId,
                Name = req.Name,
                Description = req.Description
            };
            db.Channels.Add(channel);
            await db.SaveChangesAsync(ct);
            return new Response(channel.Id);
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/channels", async (
            Request req, IMediator mediator,
            HttpContext ctx, CancellationToken ct) =>
        {
            // Injeta userId no handler antes de despachar
            // Nota: como Handler é scoped, obtemos via ISender + configuração manual
            // Alternativa simples: passar userId no request
            var userId = ctx.User.GetUserId();
            var result = await mediator.Send(req with { }, ct); // ver nota abaixo
            return Results.Created($"/channels/{result.ChannelId}", result);
        }).RequireAuthorization();
}
```

> **Nota sobre userId nos slices:** Forma mais limpa com Vertical Slice — incluir `UserId` no `Request` e populá-lo no endpoint antes de enviar ao mediator. Ajuste o `record Request` para incluir `Guid UserId` e passe `req with { UserId = ctx.User.GetUserId() }`.

**Step 2: Implementar GetChannel**

```csharp
// src/VideoApi.Application/Channels/GetChannel.cs
using MediatR;
using Microsoft.EntityFrameworkCore;
using VideoApi.Infrastructure.Persistence;

namespace VideoApi.Application.Channels;

public static class GetChannel
{
    public record Request(Guid ChannelId) : IRequest<Response>;

    public record Response(Guid Id, string Name, string? Description,
        string? AvatarUrl, long FollowerCount, DateTimeOffset CreatedAt);

    public class Handler(AppDbContext db) : IRequestHandler<Request, Response?>
    {
        public async Task<Response?> Handle(Request req, CancellationToken ct)
        {
            var channel = await db.Channels
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == req.ChannelId, ct);

            if (channel is null) return null;

            return new Response(channel.Id, channel.Name, channel.Description,
                null, // AvatarUrl: presigned URL — gerado no endpoint
                channel.FollowerCount, channel.CreatedAt);
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/v1/users/{username}/channels/{handle}", async (
            string username, string handle, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new Request { Username = username, Handle = handle }, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });
}
```

**Step 3: Implementar UpdateChannel e DeleteChannel** — mesmo padrão — validar `UserId == channel.UserId` antes de modificar.

**Step 4: Registrar endpoints no Program.cs**

```csharp
CreateChannel.MapEndpoint(app);
GetChannel.MapEndpoint(app);
UpdateChannel.MapEndpoint(app);
DeleteChannel.MapEndpoint(app);
```

**Step 5: Compilar**

```bash
dotnet build
```
Expected: `Build succeeded.`

**Step 6: Commit**

```bash
git add .
git commit -m "feat: add Channels CRUD slices"
```

---

## Task 11: ✅ Channels — Follow/Unfollow e ListChannelVideos

**Files:**
- Create: `src/VideoApi.Application/Channels/FollowChannel.cs`
- Create: `src/VideoApi.Application/Channels/UnfollowChannel.cs`
- Create: `src/VideoApi.Application/Channels/ListChannelVideos.cs`

**Step 1: Implementar FollowChannel**

```csharp
// src/VideoApi.Application/Channels/FollowChannel.cs
using MediatR;
using Microsoft.EntityFrameworkCore;
using VideoApi.Domain.Entities;
using VideoApi.Infrastructure.Persistence;

namespace VideoApi.Application.Channels;

public static class FollowChannel
{
    public record Request(Guid ChannelId, Guid UserId) : IRequest;

    public class Handler(AppDbContext db) : IRequestHandler<Request>
    {
        public async Task Handle(Request req, CancellationToken ct)
        {
            var alreadyFollowing = await db.ChannelFollowers
                .AnyAsync(f => f.ChannelId == req.ChannelId && f.UserId == req.UserId, ct);

            if (alreadyFollowing) return; // idempotente

            db.ChannelFollowers.Add(new ChannelFollower
            {
                ChannelId = req.ChannelId,
                UserId = req.UserId
            });

            // Incrementa contador atomicamente
            await db.Channels
                .Where(c => c.Id == req.ChannelId)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(c => c.FollowerCount, c => c.FollowerCount + 1), ct);

            await db.SaveChangesAsync(ct);
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/v1/users/{username}/channels/{handle}/follow", async (
            string username, string handle, IMediator mediator, HttpContext ctx, CancellationToken ct) =>
        {
            await mediator.Send(new Request { Username = username, Handle = handle, UserId = ctx.User.GetUserId() }, ct);
            return Results.NoContent();
        }).RequireAuthorization();
}
```

**Step 2: Implementar UnfollowChannel** — inverso, decrementa contador.

> **Nota:** `ListChannelVideos` movido para contexto de vídeos (Task 12+), com filtros avançados de ordenação e busca.

**Step 3: Registrar no Program.cs e compilar**

```bash
dotnet build
```
Expected: `Build succeeded.`

**Step 5: Commit**

```bash
git add .
git commit -m "feat: add Channels follow/unfollow and list videos slices"
```

---

## Task 12: ✅ Videos — CreateVideo, ConfirmUpload e ListChannelVideos

**Files:**
- Create: `src/VideoApi.Application/Videos/CreateVideo.cs`
- Create: `src/VideoApi.Application/Videos/ConfirmUpload.cs`

**Step 1: Implementar CreateVideo**

```csharp
// src/VideoApi.Application/Videos/CreateVideo.cs
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VideoApi.Application.Interfaces;
using VideoApi.Domain.Entities;
using VideoApi.Infrastructure.Persistence;
using VideoApi.Infrastructure.Settings;

namespace VideoApi.Application.Videos;

public static class CreateVideo
{
    public record Request(Guid ChannelId, Guid UserId, string Title,
        string? Description, List<string> Tags, long FileSizeBytes) : IRequest<Response>;

    public record Response(Guid VideoId, string UploadUrl, DateTimeOffset ExpiresAt);

    public class Validator : AbstractValidator<Request>
    {
        public Validator(IOptions<VideoSettings> settings)
        {
            RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
            RuleFor(x => x.Description).MaximumLength(5000);
            RuleFor(x => x.Tags).Must((req, tags) => tags.Count <= settings.Value.MaxTagsPerVideo)
                .WithMessage($"Máximo de {settings.Value.MaxTagsPerVideo} tags.");
            RuleFor(x => x.FileSizeBytes).GreaterThan(0);
        }
    }

    public class Handler(AppDbContext db, IMinioService minio, IOptions<MinioSettings> minioOpts)
        : IRequestHandler<Request, Response>
    {
        public async Task<Response> Handle(Request req, CancellationToken ct)
        {
            // Valida que o canal pertence ao usuário
            var channel = await db.Channels
                .FirstOrDefaultAsync(c => c.Id == req.ChannelId && c.UserId == req.UserId, ct);

            if (channel is null)
                throw new ValidationException("Canal não encontrado ou sem permissão.");

            var video = new Video
            {
                ChannelId = req.ChannelId,
                Title = req.Title,
                Description = req.Description,
                Tags = req.Tags.ToArray()
            };

            db.Videos.Add(video);
            await db.SaveChangesAsync(ct);

            var objectKey = $"raw/{video.Id}";
            var ttl = TimeSpan.FromHours(minioOpts.Value.UploadUrlTtlHours);
            var (uploadUrl, expiresAt) = await minio.GenerateUploadUrlAsync(objectKey, ttl, ct);

            return new Response(video.Id, uploadUrl, expiresAt);
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/videos", async (
            Request req, IMediator mediator, HttpContext ctx, CancellationToken ct) =>
        {
            var result = await mediator.Send(req with { UserId = ctx.User.GetUserId() }, ct);
            return Results.Created($"/videos/{result.VideoId}", result);
        }).RequireAuthorization();
}
```

**Step 2: Implementar ConfirmUpload**

```csharp
// src/VideoApi.Application/Videos/ConfirmUpload.cs
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using VideoApi.Application.Interfaces;
using VideoApi.Domain.Enums;
using VideoApi.Infrastructure.Persistence;

namespace VideoApi.Application.Videos;

public static class ConfirmUpload
{
    public record Request(Guid VideoId, Guid UserId) : IRequest<Response>;
    public record Response(Guid VideoId, string Status);

    public class Handler(AppDbContext db, IMinioService minio, IJobQueueService jobQueue)
        : IRequestHandler<Request, Response>
    {
        public async Task<Response> Handle(Request req, CancellationToken ct)
        {
            var video = await db.Videos
                .Include(v => v.Channel)
                .FirstOrDefaultAsync(v => v.Id == req.VideoId, ct);

            if (video is null || video.Channel.UserId != req.UserId)
                throw new ValidationException("Vídeo não encontrado ou sem permissão.");

            if (video.Status != VideoStatus.PendingUpload)
                throw new ValidationException("Vídeo não está aguardando upload.");

            var objectKey = $"raw/{video.Id}";
            var exists = await minio.ObjectExistsAsync(objectKey, ct);
            if (!exists)
                throw new ValidationException("Arquivo não encontrado no storage. Faça o upload primeiro.");

            video.Status = VideoStatus.Processing;
            video.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            // URL que o VideoProcessor vai chamar ao concluir
            var callbackUrl = $"/webhooks/video-processed";
            await jobQueue.PublishJobAsync(video.Id.ToString(), callbackUrl, ct);

            return new Response(video.Id, video.Status.ToString());
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/videos/{id:guid}/confirm-upload", async (
            Guid id, IMediator mediator, HttpContext ctx, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new Request(id, ctx.User.GetUserId()), ct)))
        .RequireAuthorization();
}
```

**Step 3: Registrar no Program.cs**

```csharp
CreateVideo.MapEndpoint(app);
ConfirmUpload.MapEndpoint(app);
```

**Step 4: Compilar**

```bash
dotnet build
```
Expected: `Build succeeded.`

**Step 5: Commit**

```bash
git add .
git commit -m "feat: add Videos/CreateVideo and Videos/ConfirmUpload slices"
```

---

## Task 13: ✅ Videos — GetVideo, DeleteVideo

**Files:**
- Create: `src/VideoApi.Application/Videos/GetVideo.cs`
- Create: `src/VideoApi.Application/Videos/DeleteVideo.cs`

**Step 1: Implementar GetVideo**

GetVideo retorna presigned GET URLs para todos os artefatos disponíveis (TTL: 1h).

```csharp
// src/VideoApi.Application/Videos/GetVideo.cs
using MediatR;
using Microsoft.EntityFrameworkCore;
using VideoApi.Application.Interfaces;
using VideoApi.Infrastructure.Persistence;

namespace VideoApi.Application.Videos;

public static class GetVideo
{
    public record Request(Guid VideoId) : IRequest<Response?>;

    public record ArtifactUrls(string? VideoUrl, string? ThumbnailsUrl,
        string? AudioUrl, string? PreviewUrl, string? HlsUrl);

    public record MetadataDto(int Width, int Height, double Fps,
        string? VideoCodec, string? AudioCodec, long Bitrate, long SizeBytes);

    public record Response(Guid Id, string Title, string? Description,
        string[] Tags, string Status, long ViewCount, long LikeCount, long DislikeCount,
        double? DurationSeconds, ArtifactUrls? Artifacts, MetadataDto? Metadata,
        Guid ChannelId, DateTimeOffset CreatedAt);

    public class Handler(AppDbContext db, IMinioService minio)
        : IRequestHandler<Request, Response?>
    {
        private static readonly TimeSpan UrlTtl = TimeSpan.FromHours(1);

        public async Task<Response?> Handle(Request req, CancellationToken ct)
        {
            var video = await db.Videos
                .AsNoTracking()
                .Include(v => v.Artifacts)
                .Include(v => v.Metadata)
                .FirstOrDefaultAsync(v => v.Id == req.VideoId, ct);

            if (video is null) return null;

            ArtifactUrls? artifacts = null;
            if (video.Artifacts is not null)
            {
                artifacts = new ArtifactUrls(
                    video.Artifacts.VideoPath is not null
                        ? await minio.GenerateDownloadUrlAsync(video.Artifacts.VideoPath, UrlTtl, ct) : null,
                    video.Artifacts.ThumbnailsPath is not null
                        ? await minio.GenerateDownloadUrlAsync(video.Artifacts.ThumbnailsPath, UrlTtl, ct) : null,
                    video.Artifacts.AudioPath is not null
                        ? await minio.GenerateDownloadUrlAsync(video.Artifacts.AudioPath, UrlTtl, ct) : null,
                    video.Artifacts.PreviewPath is not null
                        ? await minio.GenerateDownloadUrlAsync(video.Artifacts.PreviewPath, UrlTtl, ct) : null,
                    video.Artifacts.HlsPath is not null
                        ? await minio.GenerateDownloadUrlAsync(video.Artifacts.HlsPath, UrlTtl, ct) : null
                );
            }

            MetadataDto? metadata = video.Metadata is null ? null :
                new(video.Metadata.Width, video.Metadata.Height, video.Metadata.Fps,
                    video.Metadata.VideoCodec, video.Metadata.AudioCodec,
                    video.Metadata.Bitrate, video.Metadata.SizeBytes);

            // Incrementa ViewCount atomicamente
            await db.Videos
                .Where(v => v.Id == req.VideoId)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(v => v.ViewCount, v => v.ViewCount + 1), ct);

            return new Response(video.Id, video.Title, video.Description,
                video.Tags, video.Status.ToString(), video.ViewCount, video.LikeCount,
                video.DislikeCount, video.DurationSeconds, artifacts, metadata,
                video.ChannelId, video.CreatedAt);
        }
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/videos/{id:guid}", async (
            Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new Request(id), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });
}
```

**Step 2: Implementar DeleteVideo** — verificar ownership via `Channel.UserId`, deletar artefatos do MinIO se existirem, deletar do DB.

**Step 3: Registrar e compilar**

```bash
dotnet build
```
Expected: `Build succeeded.`

**Step 4: Commit**

```bash
git add .
git commit -m "feat: add Videos/GetVideo and Videos/DeleteVideo slices"
```

---

## Task 14: ✅ Videos — ListFeedVideos e ListTrendingVideos

**Files:**
- Create: `src/VideoApi.Application/Videos/ListFeedVideos.cs`
- Create: `src/VideoApi.Application/Videos/ListTrendingVideos.cs`

**Step 1: Implementar ListFeedVideos**

```csharp
// src/VideoApi.Application/Videos/ListFeedVideos.cs
// Vídeos recentes de canais seguidos pelo usuário, cursor-based
// WHERE v.ChannelId IN (SELECT ChannelId FROM ChannelFollowers WHERE UserId = @userId)
//   AND v.Status = 'Ready'
//   AND v.CreatedAt < @cursor  (se cursor fornecido)
// ORDER BY v.CreatedAt DESC LIMIT @limit + 1
```

**Step 2: Implementar ListTrendingVideos**

```csharp
// src/VideoApi.Application/Videos/ListTrendingVideos.cs
// Usa TrendingSettings para calcular score e filtrar por WindowHours
// Query SQL raw via EF:
// WHERE v.Status = 'Ready' AND v.CreatedAt > NOW() - INTERVAL '@windowHours hours'
// ORDER BY (v.ViewCount * @viewWeight + v.LikeCount * @likeWeight)
//        / POWER(EXTRACT(EPOCH FROM NOW() - v.CreatedAt) / 3600.0 + 2, @decayFactor) DESC
// Para a query de score, usar FromSqlRaw ou EF.Functions
```

**Step 3: Compilar e commitar**

```bash
dotnet build
git add .
git commit -m "feat: add Videos/ListFeedVideos and Videos/ListTrendingVideos slices"
```

---

## Task 15: ✅ Videos — VideoProcessedWebhook

**Files:**
- Create: `src/VideoApi.Application/Videos/VideoProcessedWebhook.cs`
- Create: `tests/VideoApi.UnitTests/Videos/VideoProcessedWebhookTests.cs`

Endpoint mais crítico — valida assinatura HMAC e atualiza estado do vídeo.

**Step 1: Escrever testes**

```csharp
// tests/VideoApi.UnitTests/Videos/VideoProcessedWebhookTests.cs
// Teste 1: payload com status "done" → video.Status = Ready, artifacts e metadata salvos
// Teste 2: payload com status "failed" → video.Status = Failed, error salvo
// Teste 3: assinatura HMAC inválida → retorna 401
// Teste 4: videoId não encontrado → retorna 404
```

**Step 2: Implementar o handler**

```csharp
// src/VideoApi.Application/Videos/VideoProcessedWebhook.cs
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VideoApi.Domain.Entities;
using VideoApi.Domain.Enums;
using VideoApi.Infrastructure.Persistence;
using VideoApi.Infrastructure.Settings;

namespace VideoApi.Application.Videos;

public static class VideoProcessedWebhook
{
    public record WebhookArtifacts(string? Video, string? Thumbnails,
        string? Audio, string? Preview, string? Hls);

    public record WebhookMetadata(double Duration, int Width, int Height,
        string? VideoCodec, string? AudioCodec, double Fps, long Bitrate, long Size);

    public record Request(string VideoId, string Status, string? Error,
        WebhookArtifacts? Artifacts, WebhookMetadata? Metadata) : IRequest;

    public class Handler(AppDbContext db) : IRequestHandler<Request>
    {
        public async Task Handle(Request req, CancellationToken ct)
        {
            if (!Guid.TryParse(req.VideoId, out var videoId)) return;

            var video = await db.Videos
                .Include(v => v.Artifacts)
                .Include(v => v.Metadata)
                .FirstOrDefaultAsync(v => v.Id == videoId, ct);

            if (video is null) return;

            if (req.Status == "done")
            {
                video.Status = VideoStatus.Ready;
                video.DurationSeconds = req.Metadata?.Duration;

                if (req.Artifacts is not null)
                {
                    video.Artifacts = new VideoArtifacts
                    {
                        VideoId = videoId,
                        VideoPath = req.Artifacts.Video,
                        ThumbnailsPath = req.Artifacts.Thumbnails,
                        AudioPath = req.Artifacts.Audio,
                        PreviewPath = req.Artifacts.Preview,
                        HlsPath = req.Artifacts.Hls
                    };
                }

                if (req.Metadata is not null)
                {
                    video.Metadata = new VideoMetadata
                    {
                        VideoId = videoId,
                        Width = req.Metadata.Width,
                        Height = req.Metadata.Height,
                        Fps = req.Metadata.Fps,
                        VideoCodec = req.Metadata.VideoCodec,
                        AudioCodec = req.Metadata.AudioCodec,
                        Bitrate = req.Metadata.Bitrate,
                        SizeBytes = req.Metadata.Size
                    };
                }
            }
            else
            {
                video.Status = VideoStatus.Failed;
            }

            video.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    // Valida assinatura HMAC-SHA256 do header X-Webhook-Signature
    private static bool ValidateSignature(string body, string signature, string secret)
    {
        var expected = "sha256=" + Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body)))
            .ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signature));
    }

    public static void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/webhooks/video-processed", async (
            HttpContext ctx, IMediator mediator,
            IOptions<WebhookSettings> webhookOpts, CancellationToken ct) =>
        {
            // Ler o body como string para validar HMAC antes de deserializar
            ctx.Request.EnableBuffering();
            using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync(ct);
            ctx.Request.Body.Position = 0;

            var secret = webhookOpts.Value.Secret;
            if (!string.IsNullOrEmpty(secret))
            {
                var signature = ctx.Request.Headers["X-Webhook-Signature"].FirstOrDefault() ?? "";
                if (!ValidateSignature(body, signature, secret))
                    return Results.Unauthorized();
            }

            var req = JsonSerializer.Deserialize<Request>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (req is null) return Results.BadRequest();

            await mediator.Send(req, ct);
            return Results.Ok();
        });
}
```

**Step 3: Registrar e testar**

```bash
dotnet test tests/VideoApi.UnitTests --filter "VideoProcessedWebhook"
dotnet build
```

**Step 4: Commit**

```bash
git add .
git commit -m "feat: add Videos/VideoProcessedWebhook with HMAC validation"
```

---

## Task 16: ✅ Reactions — ReactToVideo e RemoveReaction

**Files:**
- Create: `src/VideoApi.Application/Reactions/ReactToVideo.cs`
- Create: `src/VideoApi.Application/Reactions/RemoveReaction.cs`

**Step 1: Implementar ReactToVideo (upsert)**

```csharp
// src/VideoApi.Application/Reactions/ReactToVideo.cs
// Upsert: mesmo tipo → no-op; tipo diferente → atualiza + ajusta contadores; sem reação → insere + incrementa
// Transação via db.Database.BeginTransactionAsync()
```

**Step 2: Implementar RemoveReaction**

```csharp
// src/VideoApi.Application/Reactions/RemoveReaction.cs
// Deleta reação e decrementa contador correspondente
```

**Step 3: Registrar, compilar e commitar**

```bash
dotnet build
git add .
git commit -m "feat: add Reactions slices (upsert like/dislike)"
```

---

## Task 17: ✅ Comments — escopo completo

### Task 17a: ✅ Preparação do modelo

**Alterações no domínio:**
- `Comment`: adicionados `ParentCommentId? (Guid)` (self-referential FK, 1 nível), `LikeCount (int)`, `DislikeCount (int)`, `ReplyCount (int)`. Soft delete via `SoftDelete(now)`.
- `Video`: adicionado `CommentCount (int)` (counter denormalizado, atualizado via `ExecuteUpdateAsync`). Exposto em `GetVideo`.
- Nova entidade `CommentReaction`: `CommentId`, `UserId`, `Type (ReactionType)` — suporta Like e Dislike.
- Nova config: `src/VidroApi.Infrastructure/Persistence/Configurations/CommentReactionConfiguration.cs`
- Novo `DbSet<CommentReaction>` no `AppDbContext`
- Migration: `AddCommentsFeatureMigration` (sufixo `Migration` obrigatório)

---

### Task 17b: ✅ Features de comentários

**Files:**
- `src/VidroApi.Api/Features/Comments/AddComment.cs`
- `src/VidroApi.Api/Features/Comments/EditComment.cs`
- `src/VidroApi.Api/Features/Comments/DeleteComment.cs`
- `src/VidroApi.Api/Features/Comments/ListComments.cs`
- `src/VidroApi.Api/Features/Comments/ListReplies.cs`
- `src/VidroApi.Api/Features/Comments/ReactToComment.cs`
- `src/VidroApi.Api/Features/Comments/RemoveCommentReaction.cs`

**Decisões tomadas:**

`AddComment` — aceita `ParentCommentId?` opcional. Valida: pai existe, pertence ao mesmo vídeo, não é ele próprio uma resposta (sem nesting >1 nível). Incrementa `Video.CommentCount` e `ParentComment.ReplyCount` via `ExecuteUpdateAsync` em transação. Validators usam `AbstractValidator<Command>` (não `Request`).

`EditComment` — só autor pode editar. Chama `comment.Edit(content, now)`. Retorna 404 para soft-deletados.

`DeleteComment` — soft delete via `comment.SoftDelete(now)`. Decrementa `Video.CommentCount` e `ParentComment.ReplyCount` em transação. Só autor pode deletar.

`ListComments` — só comentários raiz (`ParentCommentId == null`). Param `sort=Recent|Popular`. `Recent`: cursor-based por `CreatedAt DESC`. `Popular`: lista fixa por `LikeCount DESC, CreatedAt DESC`. Soft-deletados aparecem com `content: null` e `isDeleted: true`. Dono do vídeo pode ver mesmo em vídeos privados.

`ListReplies` — respostas de comentário raiz. Cursor-based por `CreatedAt ASC`.

`ReactToComment` — upsert: sem reação → adiciona + incrementa; mesmo tipo → no-op; tipo diferente → `reaction.ChangeType(type)` + troca contadores. Suporta Like e Dislike.

`RemoveCommentReaction` — remove e decrementa contador do tipo registrado. Transação.

**Limites máximos** (via settings, não hardcoded):
- `ListCommentsSettings`: `MaxLimit=100`, `MaxPopularLimit=50`
- `ListRepliesSettings`: `MaxLimit=50`

---

### Task 17c: ✅ Testes

- Unit tests em `tests/VidroApi.UnitTests/Domain/CommentTests.cs`
- Integration tests em `tests/VidroApi.IntegrationTests/Comments/` (7 arquivos, um por feature)

---

## Task 18: ✅ Integration Tests

**Files:**
- Create: `tests/VideoApi.IntegrationTests/TestWebAppFactory.cs`
- Create: `tests/VideoApi.IntegrationTests/Auth/AuthEndpointsTests.cs`
- Create: `tests/VideoApi.IntegrationTests/Videos/VideoUploadFlowTests.cs`

**Step 1: Criar TestWebAppFactory com Testcontainers**

```csharp
// tests/VideoApi.IntegrationTests/TestWebAppFactory.cs
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using VideoApi.Infrastructure.Persistence;

namespace VideoApi.IntegrationTests;

public class TestWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().Build();
    private readonly RedisContainer _redis = new RedisBuilder().Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Substituir DbContext pelo container de teste
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor != null) services.Remove(descriptor);

            services.AddDbContext<AppDbContext>(opts =>
                opts.UseNpgsql(_postgres.GetConnectionString()));
        });
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await _redis.StartAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }
}
```

**Step 2: Teste do fluxo de auth**

```csharp
// tests/VideoApi.IntegrationTests/Auth/AuthEndpointsTests.cs
// Teste: POST /auth/register → 201
// Teste: POST /auth/login com credenciais corretas → 200 com tokens
// Teste: POST /auth/login com senha errada → 400
// Teste: POST /auth/refresh com token válido → 200 com novos tokens
```

**Step 3: Teste do fluxo de upload**

```csharp
// tests/VideoApi.IntegrationTests/Videos/VideoUploadFlowTests.cs
// Teste: criar usuário + canal + video → recebe presigned URL
// Teste: confirm-upload sem arquivo no MinIO → 400
// Teste: webhook com payload válido → video.Status = Ready
```

**Step 4: Rodar todos os testes**

```bash
dotnet test
```
Expected: todos passando.

**Step 5: Commit final**

```bash
git add .
git commit -m "test: add integration tests with Testcontainers"
```

---

## Task 19: Playlists

**Modelo:** Entidade `Playlist` com `Scope` enum (`User`/`Channel`). Playlists de usuário aceitam vídeos de qualquer canal; playlists de canal só aceitam vídeos do próprio canal.

**Entities:**
- `Playlist` — campos: `UserId` (dono sempre usuário), `ChannelId?` (preenchido quando `Scope = Channel`), `Scope` (User/Channel), `Name`, `Description?`, `Visibility` (Public/Private), `VideoCount` (denormalizado).
- `PlaylistItem` — join table `Playlist`↔`Video`. Campos: `PlaylistId`, `VideoId`, `Order` (int, posição).

**Files:**
- Create: `src/VidroApi.Domain/Entities/Playlist.cs`
- Create: `src/VidroApi.Domain/Entities/PlaylistItem.cs`
- Create: `src/VidroApi.Domain/Enums/PlaylistScope.cs`
- Create: `src/VidroApi.Domain/Enums/PlaylistVisibility.cs`
- Create: `src/VidroApi.Infrastructure/Persistence/Configurations/PlaylistConfiguration.cs`
- Create: `src/VidroApi.Infrastructure/Persistence/Configurations/PlaylistItemConfiguration.cs`
- Create: `src/VidroApi.Api/Features/Playlists/CreatePlaylist.cs`
- Create: `src/VidroApi.Api/Features/Playlists/GetPlaylist.cs`
- Create: `src/VidroApi.Api/Features/Playlists/UpdatePlaylist.cs`
- Create: `src/VidroApi.Api/Features/Playlists/DeletePlaylist.cs`
- Create: `src/VidroApi.Api/Features/Playlists/AddVideoToPlaylist.cs`
- Create: `src/VidroApi.Api/Features/Playlists/RemoveVideoFromPlaylist.cs`
- Create: `src/VidroApi.Api/Features/Playlists/ReorderPlaylistItems.cs`
- Create: `tests/VidroApi.IntegrationTests/Playlists/CreatePlaylistTests.cs`
- Create: `tests/VidroApi.IntegrationTests/Playlists/GetPlaylistTests.cs`
- Create: `tests/VidroApi.IntegrationTests/Playlists/UpdatePlaylistTests.cs`
- Create: `tests/VidroApi.IntegrationTests/Playlists/DeletePlaylistTests.cs`
- Create: `tests/VidroApi.IntegrationTests/Playlists/AddVideoToPlaylistTests.cs`
- Create: `tests/VidroApi.IntegrationTests/Playlists/RemoveVideoFromPlaylistTests.cs`
- Create: `tests/VidroApi.IntegrationTests/Playlists/ReorderPlaylistItemsTests.cs`

**Endpoints:**

| Method | Route | Auth | Descrição |
|--------|-------|------|-----------|
| POST | `/v1/playlists` | ✅ | Criar playlist (user ou channel scope) |
| GET | `/v1/playlists/{playlistId}` | opcional | Buscar playlist com itens; private → 404 para não-dono |
| PUT | `/v1/playlists/{playlistId}` | ✅ dono | Atualizar nome/descrição/visibilidade |
| DELETE | `/v1/playlists/{playlistId}` | ✅ dono | Remover playlist e seus itens |
| POST | `/v1/playlists/{playlistId}/items` | ✅ dono | Adicionar vídeo à playlist |
| DELETE | `/v1/playlists/{playlistId}/items/{videoId}` | ✅ dono | Remover vídeo da playlist |
| PUT | `/v1/playlists/{playlistId}/items/order` | ✅ dono | Reordenar itens (recebe lista ordenada de videoIds) |

**Regras de negócio:**
- `Scope = Channel` exige `ChannelId`; valida usuário é dono do canal.
- `AddVideoToPlaylist` com `Scope = Channel` valida vídeo pertence ao canal → 422 se não.
- `VideoCount` denormalizado, atualizado via `ExecuteUpdateAsync` ao adicionar/remover.
- `Order`: reordenação recebe array de `videoId` na ordem desejada, reatribui `Order = índice`.
- `GetPlaylist` retorna itens ordenados por `Order`, campos básicos do vídeo (id, title, thumbnailUrl, duration).
- Playlist privada → 404 para não-donos.
- Vídeo duplicado na mesma playlist → 409.
- Erros específicos em `Errors.Playlist.cs`: `NotOwner`, `VideoAlreadyInPlaylist`, `VideoNotInPlaylist`, `VideoNotFromChannel`.

**Migration:**

```bash
dotnet ef migrations add AddPlaylists --project src/VidroApi.Infrastructure --startup-project src/VidroApi.Api --output-dir Persistence/Migrations
```

---

## Task 20: ✅ Videos — UploadVideoThumbnail

**Descrição:** Dono do vídeo faz upload de thumbnail personalizada, substituindo as geradas pelo VideoProcessor.

**Fluxo:** Mesmo padrão do upload de vídeo — API gera presigned PUT URL no MinIO, cliente faz upload direto. Sem webhook; thumbnail disponível imediatamente.

**Caminho no MinIO:** `thumbnails/{videoId}/custom.jpg` (separado de `thumbnails/{videoId}/thumb{n}.jpg`).

**Entities:**
- `VideoArtifacts` — adicionar campo `CustomThumbnailPath` (`string?`).

**Files:**
- Update: `src/VidroApi.Domain/Entities/VideoArtifacts.cs` (campo `CustomThumbnailPath`)
- Create: `src/VidroApi.Api/Features/Videos/UploadVideoThumbnail.cs`
- Create: `tests/VidroApi.IntegrationTests/Videos/UploadVideoThumbnailTests.cs`
- Migration para `custom_thumbnail_path` em `video_artifacts`

**Endpoints:**

| Method | Route | Auth | Descrição |
|--------|-------|------|-----------|
| POST | `/v1/videos/{videoId}/thumbnail` | ✅ dono | Gera presigned URL para upload de thumbnail personalizada |

**Regras de negócio:**
- Vídeo deve existir e usuário ser dono → 403/404 conforme padrão.
- Não exige `Status = Ready`.
- Retorna `uploadUrl` e `expiresAt`.
- `CustomThumbnailPath` preenchido imediatamente (path determinístico: `thumbnails/{videoId}/custom.jpg`).

---

## Task 21: ✅ Users — Foto de Perfil

**Descrição:** Upload de foto de perfil via presigned URL.

**Caminho no MinIO:** `avatars/{userId}`.

**Entities:**
- `User` — adicionar campo `AvatarPath` (`string?`).

**Files:**
- Update: `src/VidroApi.Domain/Entities/User.cs` (campo `AvatarPath`, método `SetAvatar(string path, DateTimeOffset now)`)
- Update: `src/VidroApi.Infrastructure/Persistence/Configurations/UserConfiguration.cs` (mapear `avatar_path`)
- Create: `src/VidroApi.Api/Features/Users/UploadAvatar.cs`
- Create: `tests/VidroApi.IntegrationTests/Users/UploadAvatarTests.cs`
- Migration para `avatar_path` em `users`

**Endpoints:**

| Method | Route | Auth | Descrição |
|--------|-------|------|-----------|
| POST | `/v1/users/me/avatar` | ✅ | Gera presigned URL para upload de avatar |

**Regras de negócio:**
- Retorna `uploadUrl` e `expiresAt`.
- Path determinístico (`avatars/{userId}`) → `AvatarPath` gravado antes do upload.
- Upload sobrescreve avatar anterior (path fixo, novo upload sobrescreve objeto).

---

## Task 21.1: ✅ Channels — Foto de Perfil

**Descrição:** Avatar de canal via presigned URL.

**Caminho no MinIO:** `avatars/channels/{channelId}`.

**Entities:**
- `Channel` — adicionar campo `AvatarPath` (`string?`), método `SetAvatar(string path)`.

**Files:**
- Update: `src/VidroApi.Domain/Entities/Channel.cs` (campo `AvatarPath`, método `SetAvatar`)
- Create: `src/VidroApi.Api/Features/Channels/UploadChannelAvatar.cs`
- Update: `src/VidroApi.Api/Features/Channels/GetChannel.cs` (retornar `AvatarUrl` presigned)
- Update: `src/VidroApi.Api/Features/Channels/ListMyChannels.cs` (retornar `AvatarUrl` presigned)
- Create: `tests/VidroApi.IntegrationTests/Channels/UploadChannelAvatarTests.cs`
- Migration para `avatar_path` em `channels`

**Endpoints:**

| Method | Route | Auth | Descrição |
|--------|-------|------|-----------|
| POST | `/v1/channels/{handle}/avatar` | ✅ dono | Gera presigned URL para upload de avatar do canal |

**Regras de negócio:**
- Só dono pode fazer upload → 403.
- Retorna `uploadUrl` e `expiresAt`.
- Path determinístico → `AvatarPath` gravado imediatamente.
- Rota usa `{handle}` (string única por usuário).
- `GetChannel` retorna `avatarUrl` (presigned, TTL 1h) ou `null`.
- `ListMyChannels` retorna `avatarUrl` para cada canal.
- **Integração com vídeos:** todos os endpoints de listagem/obtenção retornam `channelAvatarUrl` (presigned):
  - `GetVideo`, `ListChannelVideos`, `ListFeedVideos`, `ListTrendingVideos`, `SearchVideos`

---

## Task 22: Videos — Contagem de Visualizações ✅

**Decisão:** Opção D — endpoint `POST /v1/videos/{videoId}/view` com deduplicação via Redis.

**Implementado em:** `src/VidroApi.Api/Features/Videos/RegisterVideoView.cs`

- Endpoint `POST /v1/videos/{videoId}/view` — sem autenticação obrigatória
- Incremento atômico via `ExecuteUpdateAsync`
- Deduplicação: chave Redis `view:{videoId}:{userId}` (autenticados) ou `view:{videoId}:{ip}` (anônimos), TTL via `VideoSettings:ViewDeduplicationWindowHours` (padrão: 1h)
- View duplicada dentro da janela → `200 OK` silencioso

---

## Resumo dos Endpoints

Após todas as tasks:

```csharp
// Auth
Register.MapEndpoint(app);
Login.MapEndpoint(app);
RefreshToken.MapEndpoint(app);

// Channels
CreateChannel.MapEndpoint(app);
GetChannel.MapEndpoint(app);
UpdateChannel.MapEndpoint(app);
DeleteChannel.MapEndpoint(app);
FollowChannel.MapEndpoint(app);
UnfollowChannel.MapEndpoint(app);
UploadChannelAvatar.MapEndpoint(app);

// Videos
CreateVideo.MapEndpoint(app);
ConfirmUpload.MapEndpoint(app);
ListChannelVideos.MapEndpoint(app);
GetVideo.MapEndpoint(app);
DeleteVideo.MapEndpoint(app);
ListFeedVideos.MapEndpoint(app);
ListTrendingVideos.MapEndpoint(app);
VideoProcessedWebhook.MapEndpoint(app);
UploadVideoThumbnail.MapEndpoint(app);

// Users
UploadAvatar.MapEndpoint(app);

// Reactions
ReactToVideo.MapEndpoint(app);
RemoveReaction.MapEndpoint(app);

// Comments
AddComment.MapEndpoint(app);
ListComments.MapEndpoint(app);
DeleteComment.MapEndpoint(app);

// Playlists
CreatePlaylist.MapEndpoint(app);
GetPlaylist.MapEndpoint(app);
UpdatePlaylist.MapEndpoint(app);
DeletePlaylist.MapEndpoint(app);
AddVideoToPlaylist.MapEndpoint(app);
RemoveVideoFromPlaylist.MapEndpoint(app);
ReorderPlaylistItems.MapEndpoint(app);
```

---

## Ordem de Implementação Recomendada

1. Tasks 1–7 (scaffold, domain, infra, migrations) — base
2. Tasks 8–9 (Auth) — necessário para testar outros endpoints
3. Tasks 10–11 (Channels) — necessário para criar vídeos
4. Tasks 12–15 (Videos) — core da plataforma
5. Task 20 (UploadVideoThumbnail) — depende de vídeo pronto
6. Tasks 21 e 21.1 (avatars usuário/canal) — independentes
7. Task 22 (ViewCount) — ✅ implementado
8. Tasks 16–17 (Reactions + Comments) — features sociais
9. Task 19 (Playlists) — depende de vídeos
10. Task 18 (Integration Tests) — validação end-to-end

---

## Task 23: ✅ Users — GetCurrentUser

**Implementado em:** `src/VidroApi.Api/Features/Users/GetCurrentUser.cs`

- Endpoint `GET /v1/users/me` — requer autenticação
- Retorna: `UserId`, `Username`, `Email`, `AvatarUrl`
- Gera URL presignada para avatar via MinIO

---

## Task 24: ✅ Channels — ListUserChannels

**Implementado em:** `src/VidroApi.Api/Features/Channels/ListUserChannels.cs`

- Endpoint `GET /v1/users/{username}/channels` — sem autenticação obrigatória
- Retorna lista de `ChannelSummary`: id, nome, descrição, contagem de seguidores, avatar URL
- URLs presignadas para avatars via MinIO
- 404 se usuário não existe

---

## Task 25: ✅ Videos — SearchVideos

**Implementado em:** `src/VidroApi.Api/Features/Videos/SearchVideos.cs`

- Endpoint `POST /v1/videos/search` — sem autenticação obrigatória
- Entrada: `Query` (obrigatória), `Cursor` (paginação), `Limit`
- Retorna `VideoSummary`: id, canal, título, tags, contadores, thumbnails, data
- Paginação cursor-based com `NextCursor`
- Validação: query entre `SearchSettings:MinQueryLength` e `SearchSettings:MaxQueryLength`

---

## Task 26: ✅ Channels — Melhorias em GetChannel

**Implementado em:** `src/VidroApi.Api/Features/Channels/GetChannel.cs`

- Campos adicionais na `Response`:
  - `OwnerId`, `OwnerUsername`, `OwnerAvatarUrl` (presigned via MinIO)

---

## Task 27: ✅ Videos — Suporte a Múltiplas Thumbnails

**Implementado em:** `ListChannelVideos`, `ListFeedVideos`, `ListTrendingVideos`, `SearchVideos`, `GetVideo`

- Cada `VideoSummary` inclui `List<string> ThumbnailUrls`
- Geradas de `VideoMetadata.ThumbnailPaths`, presignadas via MinIO

---

## Task 28: ✅ Comments — Melhorias na Feature de Comentários

**Implementado em:**
- `src/VidroApi.Api/Features/Comments/EditComment.cs`
- `src/VidroApi.Api/Features/Comments/ListReplies.cs`
- `src/VidroApi.Api/Features/Comments/ReactToComment.cs`
- `src/VidroApi.Api/Features/Comments/RemoveCommentReaction.cs`

**Endpoints adicionados:**
- `PATCH /v1/comments/{commentId}` — editar (só autor)
- `GET /v1/comments/{commentId}/replies` — listar respostas cursor-based
- `POST /v1/comments/{commentId}/reactions` — adicionar/alterar reação
- `DELETE /v1/comments/{commentId}/reactions/{reactionType}` — remover reação

---

## Resumo dos Endpoints (Atualizado)

```csharp
// Auth
SignUp.MapEndpoint(app);
SignIn.MapEndpoint(app);
RenewToken.MapEndpoint(app);
SignOut.MapEndpoint(app);

// Channels
CreateChannel.MapEndpoint(app);
GetChannel.MapEndpoint(app);
UpdateChannel.MapEndpoint(app);
DeleteChannel.MapEndpoint(app);
ListUserChannels.MapEndpoint(app);
FollowChannel.MapEndpoint(app);
UnfollowChannel.MapEndpoint(app);
UploadChannelAvatar.MapEndpoint(app);

// Videos
CreateVideo.MapEndpoint(app);
MinioUploadCompleted.MapEndpoint(app);
ListChannelVideos.MapEndpoint(app);
GetVideo.MapEndpoint(app);
DeleteVideo.MapEndpoint(app);
ListFeedVideos.MapEndpoint(app);
ListTrendingVideos.MapEndpoint(app);
SearchVideos.MapEndpoint(app);
VideoProcessed.MapEndpoint(app);
UploadVideoThumbnail.MapEndpoint(app);
RegisterVideoView.MapEndpoint(app);

// Users
GetCurrentUser.MapEndpoint(app);
UploadAvatar.MapEndpoint(app);

// Reactions (Videos)
ReactToVideo.MapEndpoint(app);
RemoveReaction.MapEndpoint(app);

// Comments
AddComment.MapEndpoint(app);
EditComment.MapEndpoint(app);
ListComments.MapEndpoint(app);
ListReplies.MapEndpoint(app);
DeleteComment.MapEndpoint(app);
ReactToComment.MapEndpoint(app);
RemoveCommentReaction.MapEndpoint(app);
```