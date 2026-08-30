using System.Diagnostics.CodeAnalysis;

namespace VidroApi.Domain.Entities;

public class VideoArtifacts : BaseEntity
{
    public const int PathMaxLength = 500;

    // ReSharper disable once UnusedMember.Local
    [ExcludeFromCodeCoverage]
    private VideoArtifacts() { }

    // Only ProcessedPath is required: the Processor reports success even when a
    // non-critical step (thumbnails/audio/preview/streaming) fails, and the
    // webhook then omits that artifact. See VidroProcessor design-decisions.md.
    public VideoArtifacts(Guid videoId, string processedPath, string? previewPath,
        string? hlsPath, string? audioPath, List<string>? thumbnailPaths, DateTimeOffset now)
        : base(now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processedPath);
        if (processedPath.Length > PathMaxLength)
            throw new ArgumentException($"ProcessedPath cannot exceed {PathMaxLength} characters.", nameof(processedPath));
        if (previewPath?.Length > PathMaxLength)
            throw new ArgumentException($"PreviewPath cannot exceed {PathMaxLength} characters.", nameof(previewPath));
        if (hlsPath?.Length > PathMaxLength)
            throw new ArgumentException($"HlsPath cannot exceed {PathMaxLength} characters.", nameof(hlsPath));
        if (audioPath?.Length > PathMaxLength)
            throw new ArgumentException($"AudioPath cannot exceed {PathMaxLength} characters.", nameof(audioPath));

        VideoId = videoId;
        ProcessedPath = processedPath;
        PreviewPath = NullIfBlank(previewPath);
        HlsPath = NullIfBlank(hlsPath);
        AudioPath = NullIfBlank(audioPath);
        ThumbnailPaths = thumbnailPaths ?? [];
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    public Guid VideoId { get; init; }
    public string ProcessedPath { get; init; } = null!;
    public string? PreviewPath { get; init; }
    public string? HlsPath { get; init; }
    public string? AudioPath { get; init; }
    public List<string> ThumbnailPaths { get; init; } = [];
    public string? CustomThumbnailPath { get; private set; }

    public void SetCustomThumbnailPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.Length > PathMaxLength)
            throw new ArgumentException($"CustomThumbnailPath cannot exceed {PathMaxLength} characters.", nameof(path));
        CustomThumbnailPath = path;
    }

    // Navigation property
    public Video Video { get; init; } = null!;
}
