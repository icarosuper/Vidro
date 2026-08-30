using FluentAssertions;
using VidroApi.Domain.Entities;

namespace VidroApi.UnitTests.Domain;

public class ChannelTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_ShouldSetRequiredProperties()
    {
        var channel = new Channel(UserId, "my-channel", "My Channel", "A description", Now);

        channel.UserId.Should().Be(UserId);
        channel.Handle.Should().Be("my-channel");
        channel.Name.Should().Be("My Channel");
        channel.Description.Should().Be("A description");
        channel.FollowerCount.Should().Be(0);
        channel.CreatedAt.Should().Be(Now);
        channel.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void Constructor_ShouldAllowNullDescription()
    {
        var channel = new Channel(UserId, "my-channel", "My Channel", null, Now);

        channel.Description.Should().BeNull();
    }

    [Fact]
    public void UpdateDetails_ShouldUpdateNameAndDescriptionAndSetUpdatedAt()
    {
        var channel = new Channel(UserId, "my-channel", "Old Name", "Old description", Now);
        var updatedAt = Now.AddDays(1);

        channel.UpdateDetails("New Name", "New description", updatedAt);

        channel.Name.Should().Be("New Name");
        channel.Description.Should().Be("New description");
        channel.UpdatedAt.Should().Be(updatedAt);
    }
}
