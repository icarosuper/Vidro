using System.Diagnostics.CodeAnalysis;

namespace VidroApi.Domain.Entities;

public class PlaylistItem : BaseEntity
{
    // ReSharper disable once UnusedMember.Local
    [ExcludeFromCodeCoverage]
    private PlaylistItem() { }

    public PlaylistItem(Guid playlistId, Guid? videoId, DateTimeOffset now)
        : base(now)
    {
        PlaylistId = playlistId;
        VideoId = videoId;
    }

    public Guid PlaylistId { get; init; }
    public Guid? VideoId { get; init; }

    // Navigation properties
    public Playlist Playlist { get; init; } = null!;
    public Video? Video { get; init; }
}
