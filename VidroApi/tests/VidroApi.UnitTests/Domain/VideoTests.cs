using FluentAssertions;
using VidroApi.Domain.Entities;
using VidroApi.Domain.Enums;

namespace VidroApi.UnitTests.Domain;

public class VideoTests
{
    private static readonly Guid ChannelId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);
    private static readonly List<string> Tags = ["tech", "csharp"];

    [Fact]
    public void Constructor_ShouldSetRequiredProperties()
    {
        var video = new Video(ChannelId, "My Video", "A description", Tags, VideoVisibility.Public, Now.AddHours(2), Now);

        video.ChannelId.Should().Be(ChannelId);
        video.Title.Should().Be("My Video");
        video.Description.Should().Be("A description");
        video.Tags.Should().BeEquivalentTo(Tags);
        video.Visibility.Should().Be(VideoVisibility.Public);
        video.Status.Should().Be(VideoStatus.PendingUpload);
        video.UploadExpiresAt.Should().Be(Now.AddHours(2));
        video.ViewCount.Should().Be(0);
        video.LikeCount.Should().Be(0);
        video.DislikeCount.Should().Be(0);
        video.CreatedAt.Should().Be(Now);
        video.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void UpdateDetails_ShouldUpdateFieldsAndSetUpdatedAt()
    {
        var video = new Video(ChannelId, "Old Title", null, [], VideoVisibility.Public, Now.AddHours(2), Now);
        var updatedAt = Now.AddDays(1);

        video.UpdateDetails("New Title", "New description", ["new-tag"], VideoVisibility.Private, updatedAt);

        video.Title.Should().Be("New Title");
        video.Description.Should().Be("New description");
        video.Tags.Should().BeEquivalentTo(["new-tag"]);
        video.Visibility.Should().Be(VideoVisibility.Private);
        video.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void ChangeVisibility_ShouldUpdateVisibilityAndSetUpdatedAt()
    {
        var video = new Video(ChannelId, "My Video", null, [], VideoVisibility.Public, Now.AddHours(2), Now);
        var updatedAt = Now.AddHours(1);

        video.ChangeVisibility(VideoVisibility.Unlisted, updatedAt);

        video.Visibility.Should().Be(VideoVisibility.Unlisted);
        video.UpdatedAt.Should().Be(updatedAt);
    }

    [Theory]
    [InlineData(VideoStatus.Processing)]
    [InlineData(VideoStatus.Ready)]
    [InlineData(VideoStatus.Failed)]
    public void StatusMethods_ShouldUpdateStatusAndSetUpdatedAt(VideoStatus expectedStatus)
    {
        var video = new Video(ChannelId, "My Video", null, [], VideoVisibility.Public, Now.AddHours(2), Now);
        var updatedAt = Now.AddHours(1);

        if (expectedStatus == VideoStatus.Processing) video.MarkAsProcessing(updatedAt);
        else if (expectedStatus == VideoStatus.Ready) video.MarkAsReady(updatedAt);
        else video.MarkAsFailed(updatedAt);

        video.Status.Should().Be(expectedStatus);
        video.UpdatedAt.Should().Be(updatedAt);
    }

}
