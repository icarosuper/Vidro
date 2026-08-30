using System.ComponentModel.DataAnnotations;

namespace VidroApi.Infrastructure.Settings;

public class MinioSettings
{
    [Required]
    public string Endpoint { get; set; } = null!;

    [Required]
    public string AccessKey { get; set; } = null!;

    [Required]
    public string SecretKey { get; set; } = null!;

    [Required]
    public string BucketName { get; set; } = null!;

    public bool UseSsl { get; set; }

    /// <summary>
    /// Host:port the browser uses to reach MinIO, when it differs from <see cref="Endpoint"/>
    /// (Docker: API talks to `minio:9000`, the browser to `localhost:9000`). Presigned URLs sign
    /// the Host header, so they must be generated for the host the browser will actually send.
    /// Null/empty = same as <see cref="Endpoint"/>.
    /// </summary>
    public string? PublicEndpoint { get; set; }

    [Required, Range(1, int.MaxValue)]
    public int UploadUrlTtlHours { get; set; }

    [Required, Range(1, int.MaxValue)]
    public int ThumbnailUrlTtlHours { get; set; }

    [Required, Range(1, int.MaxValue)]
    public int VideoUrlTtlHours { get; set; }
}
