namespace VidroApi.Infrastructure.Services;

/// <summary>
/// MinIO object-key layout shared with the worker. Locked by
/// <c>contracts/minio-paths.json</c> (see contracts/README.md).
/// </summary>
public static class MinioObjectKeys
{
    public const string RawPrefix = "raw";
    public const int ThumbnailCount = 5;

    public static string Raw(Guid videoId) => $"{RawPrefix}/{videoId}";
    public static string RawArchived(Guid videoId) => $"raw-archived/{videoId}";
    public static string Processed(Guid videoId) => $"processed/{videoId}_processed";
    public static string Audio(Guid videoId) => $"audio/{videoId}.mp3";
    public static string Preview(Guid videoId) => $"preview/{videoId}_preview.mp4";
    public static string ThumbnailsPrefix(Guid videoId) => $"thumbnails/{videoId}";
    public static string ThumbnailsCleanupPrefix(Guid videoId) => $"{ThumbnailsPrefix(videoId)}/";
    public static string CustomThumbnail(Guid videoId) => $"{ThumbnailsPrefix(videoId)}/custom.jpg";
    public static string HlsPrefix(Guid videoId) => $"hls/{videoId}";

    public static IReadOnlyList<string> ThumbnailPaths(Guid videoId)
    {
        var prefix = ThumbnailsPrefix(videoId);
        var paths = new string[ThumbnailCount];
        for (var index = 1; index <= ThumbnailCount; index++)
            paths[index - 1] = $"{prefix}/thumb_{index:D3}.jpg";
        return paths;
    }

    /// <summary>
    /// MinIO notifies as <c>{bucket}/raw/{videoId}</c>. Anything else is not an upload completing.
    /// </summary>
    public static Guid? TryParseVideoIdFromNotificationKey(string key)
    {
        var segments = key.Split('/');
        var rawSegmentIndex = Array.IndexOf(segments, RawPrefix);
        if (rawSegmentIndex < 0 || rawSegmentIndex + 1 >= segments.Length)
            return null;

        var videoIdSegment = segments[rawSegmentIndex + 1];
        return Guid.TryParse(videoIdSegment, out var videoId) ? videoId : null;
    }
}
