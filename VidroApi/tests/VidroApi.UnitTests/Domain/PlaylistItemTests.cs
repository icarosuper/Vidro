using FluentAssertions;
using VidroApi.Domain.Entities;

namespace VidroApi.UnitTests.Domain;

public class PlaylistItemTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_ShouldSetRequiredProperties()
    {
        var playlistId = Guid.NewGuid();
        var videoId = Guid.NewGuid();

        var item = new PlaylistItem(playlistId, videoId, Now);

        item.PlaylistId.Should().Be(playlistId);
        item.VideoId.Should().Be(videoId);
        item.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public void Constructor_ShouldAllowNullVideoId()
    {
        var playlistId = Guid.NewGuid();

        var item = new PlaylistItem(playlistId, null, Now);

        item.PlaylistId.Should().Be(playlistId);
        item.VideoId.Should().BeNull();
        item.CreatedAt.Should().Be(Now);
    }
}
