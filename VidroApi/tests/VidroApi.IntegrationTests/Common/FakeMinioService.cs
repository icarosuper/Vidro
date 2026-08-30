using VidroApi.Application.Abstractions;

namespace VidroApi.IntegrationTests.Common;

public class FakeMinioService : IMinioService
{
    public Task<(string Url, DateTimeOffset ExpiresAt)> GenerateUploadUrlAsync(
        string objectKey, TimeSpan ttl, CancellationToken ct = default)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(ttl);
        var fakeUrl = $"https://minio.fake/{objectKey}?signature=fake&expires={expiresAt:O}";
        return Task.FromResult((fakeUrl, expiresAt));
    }

    public Task<string> GenerateDownloadUrlAsync(
        string objectKey, TimeSpan ttl, CancellationToken ct = default)
    {
        var fakeUrl = $"https://minio.fake/{objectKey}?signature=fake";
        return Task.FromResult(fakeUrl);
    }

    public Task<bool> ObjectExistsAsync(string objectKey, CancellationToken ct = default) =>
        Task.FromResult(false);

    public Task DeleteObjectAsync(string objectKey, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task DeleteObjectsByPrefixAsync(string prefix, CancellationToken ct = default) =>
        Task.CompletedTask;
}
