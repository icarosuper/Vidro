using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VidroApi.Application.Abstractions;
using VidroApi.Domain.Entities;
using VidroApi.Infrastructure.Persistence;
using VidroApi.Infrastructure.Settings;

namespace VidroApi.Api.BackgroundServices;

public class StorageCleanupService(
    IServiceScopeFactory scopeFactory,
    IOptions<StorageCleanupSettings> options,
    ILogger<StorageCleanupService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(options.Value.IntervalMinutes);
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ProcessPendingCleanupsAsync(stoppingToken);
        }
    }

    private async Task ProcessPendingCleanupsAsync(CancellationToken ct)
    {
        logger.LogInformation("Starting storage cleanup");

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var minio = scope.ServiceProvider.GetRequiredService<IMinioService>();

            var pending = await db.PendingStorageCleanups
                .OrderBy(p => p.CreatedAt)
                .Take(options.Value.BatchSize)
                .ToListAsync(ct);

            logger.LogInformation("Found {Count} pending storage cleanups", pending.Count);

            foreach (var cleanup in pending)
            {
                await ProcessCleanupAsync(cleanup, db, minio, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error during storage cleanup");
        }
    }

    private async Task ProcessCleanupAsync(
        PendingStorageCleanup cleanup,
        AppDbContext db,
        IMinioService minio,
        CancellationToken ct)
    {
        try
        {
            if (cleanup.IsPrefix)
                await minio.DeleteObjectsByPrefixAsync(cleanup.ObjectPath, ct);
            else
                await minio.DeleteObjectAsync(cleanup.ObjectPath, ct);

            await db.PendingStorageCleanups
                .Where(p => p.Id == cleanup.Id)
                .ExecuteDeleteAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to clean up storage object {ObjectPath}", cleanup.ObjectPath);
        }
    }
}
