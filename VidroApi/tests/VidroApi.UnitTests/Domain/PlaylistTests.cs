using FluentAssertions;
using VidroApi.Domain.Entities;
using VidroApi.Domain.Enums;

namespace VidroApi.UnitTests.Domain;

public class PlaylistTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_ShouldSetRequiredProperties()
    {
        var playlist = new Playlist(UserId, null, "My Playlist", "A description",
            PlaylistScope.User, PlaylistVisibility.Public, Now);

        playlist.UserId.Should().Be(UserId);
        playlist.ChannelId.Should().BeNull();
        playlist.Name.Should().Be("My Playlist");
        playlist.Description.Should().Be("A description");
        playlist.Scope.Should().Be(PlaylistScope.User);
        playlist.Visibility.Should().Be(PlaylistVisibility.Public);
        playlist.VideoCount.Should().Be(0);
        playlist.CreatedAt.Should().Be(Now);
        playlist.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithChannelScope_ShouldSetChannelId()
    {
        var channelId = Guid.NewGuid();

        var playlist = new Playlist(UserId, channelId, "Channel Playlist", null,
            PlaylistScope.Channel, PlaylistVisibility.Public, Now);

        playlist.ChannelId.Should().Be(channelId);
        playlist.Scope.Should().Be(PlaylistScope.Channel);
    }

    [Fact]
    public void Constructor_ShouldAllowNullDescription()
    {
        var playlist = new Playlist(UserId, null, "My Playlist", null,
            PlaylistScope.User, PlaylistVisibility.Private, Now);

        playlist.Description.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithEmptyName_ShouldThrow()
    {
        var act = () => new Playlist(UserId, null, "   ", null,
            PlaylistScope.User, PlaylistVisibility.Public, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithNameTooLong_ShouldThrow()
    {
        var longName = new string('a', Playlist.NameMaxLength + 1);

        var act = () => new Playlist(UserId, null, longName, null,
            PlaylistScope.User, PlaylistVisibility.Public, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithDescriptionTooLong_ShouldThrow()
    {
        var longDescription = new string('a', Playlist.DescriptionMaxLength + 1);

        var act = () => new Playlist(UserId, null, "Name", longDescription,
            PlaylistScope.User, PlaylistVisibility.Public, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UpdateDetails_ShouldUpdateNameDescriptionVisibilityAndSetUpdatedAt()
    {
        var playlist = new Playlist(UserId, null, "Old Name", "Old description",
            PlaylistScope.User, PlaylistVisibility.Public, Now);
        var updatedAt = Now.AddDays(1);

        playlist.UpdateDetails("New Name", "New description", PlaylistVisibility.Private, updatedAt);

        playlist.Name.Should().Be("New Name");
        playlist.Description.Should().Be("New description");
        playlist.Visibility.Should().Be(PlaylistVisibility.Private);
        playlist.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void UpdateDetails_WithEmptyName_ShouldThrow()
    {
        var playlist = new Playlist(UserId, null, "Name", null,
            PlaylistScope.User, PlaylistVisibility.Public, Now);

        var act = () => playlist.UpdateDetails("", null, PlaylistVisibility.Public, Now.AddDays(1));

        act.Should().Throw<ArgumentException>();
    }
}
