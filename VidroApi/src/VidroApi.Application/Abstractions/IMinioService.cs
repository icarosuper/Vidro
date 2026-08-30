namespace VidroApi.Application.Abstractions;

public interface IMinioService
{
    Task<(string Url, DateTimeOffset ExpiresAt)> GenerateUploadUrlAsync(
        string objectKey, TimeSpan ttl, CancellationToken ct = default);

    Task<string> GenerateDownloadUrlAsync(
        string objectKey, TimeSpan ttl, CancellationToken ct = default);

    Task<bool> ObjectExistsAsync(string objectKey, CancellationToken ct = default);

    Task DeleteObjectAsync(string objectKey, CancellationToken ct = default);

    Task DeleteObjectsByPrefixAsync(string prefix, CancellationToken ct = default);
}
