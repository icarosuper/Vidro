using System.Diagnostics.CodeAnalysis;

namespace VidroApi.Domain.Entities;

public class VideoMetadata : BaseEntity
{
    public const int CodecMaxLength = 50;

    // ReSharper disable once UnusedMember.Local
    [ExcludeFromCodeCoverage]
    private VideoMetadata() { }

    public VideoMetadata(Guid videoId, long fileSizeBytes, double durationSeconds,
        int width, int height, string codec, DateTimeOffset now)
        : base(now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codec);
        if (codec.Length > CodecMaxLength)
            throw new ArgumentException($"Codec cannot exceed {CodecMaxLength} characters.", nameof(codec));

        VideoId = videoId;
        FileSizeBytes = fileSizeBytes;
        DurationSeconds = durationSeconds;
        Width = width;
        Height = height;
        Codec = codec;
    }

    public Guid VideoId { get; init; }
    public long FileSizeBytes { get; init; }
    public double DurationSeconds { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string Codec { get; init; } = null!;

    // Navigation property
    public Video Video { get; init; } = null!;
}
