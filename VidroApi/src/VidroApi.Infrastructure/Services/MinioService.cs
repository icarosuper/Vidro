using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using VidroApi.Application.Abstractions;
using VidroApi.Infrastructure.Settings;

namespace VidroApi.Infrastructure.Services;

public class MinioService(
    IMinioClient minioClient,
    [FromKeyedServices(MinioService.PresignClientKey)] IMinioClient presignClient,
    IOptions<MinioSettings> options) : IMinioService
{
    public const string PresignClientKey = "minio-presign";

    private readonly MinioSettings _settings = options.Value;

    public async Task<(string Url, DateTimeOffset ExpiresAt)> GenerateUploadUrlAsync(
        string objectKey, TimeSpan ttl, CancellationToken ct = default)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(ttl);
        var url = await presignClient.PresignedPutObjectAsync(
            new PresignedPutObjectArgs()
                .WithBucket(_settings.BucketName)
                .WithObject(objectKey)
                .WithExpiry((int)ttl.TotalSeconds));
        return (url, expiresAt);
    }

    public async Task<string> GenerateDownloadUrlAsync(
        string objectKey, TimeSpan ttl, CancellationToken ct = default)
    {
        return await presignClient.PresignedGetObjectAsync(
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

    public async Task DeleteObjectAsync(string objectKey, CancellationToken ct = default)
    {
        await minioClient.RemoveObjectAsync(
            new RemoveObjectArgs()
                .WithBucket(_settings.BucketName)
                .WithObject(objectKey), ct);
    }

    public async Task DeleteObjectsByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        var objects = new List<string>();

        var listArgs = new ListObjectsArgs()
            .WithBucket(_settings.BucketName)
            .WithPrefix(prefix)
            .WithRecursive(true);

        await foreach (var item in minioClient.ListObjectsEnumAsync(listArgs, ct))
            objects.Add(item.Key);

        if (objects.Count == 0)
            return;

        var removeArgs = new RemoveObjectsArgs()
            .WithBucket(_settings.BucketName)
            .WithObjects(objects);

        await minioClient.RemoveObjectsAsync(removeArgs, ct);
    }
}
