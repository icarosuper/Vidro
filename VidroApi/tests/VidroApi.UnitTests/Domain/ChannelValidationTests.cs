using FluentAssertions;
using VidroApi.Domain.Entities;

namespace VidroApi.UnitTests.Domain;

public class ChannelValidationTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_ShouldThrow_WhenHandleIsNullOrWhiteSpace(string handle)
    {
        var act = () => new Channel(UserId, handle, "My Channel", null, Now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenHandleTooShort()
    {
        var handle = new string('a', Channel.HandleMinLength - 1);
        var act = () => new Channel(UserId, handle, "My Channel", null, Now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenHandleExceedsMaxLength()
    {
        var handle = new string('a', Channel.HandleMaxLength + 1);
        var act = () => new Channel(UserId, handle, "My Channel", null, Now);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("My Channel")]
    [InlineData("UPPERCASE")]
    [InlineData("has space")]
    [InlineData("has_underscore")]
    [InlineData("has@symbol")]
    public void Constructor_ShouldThrow_WhenHandleContainsInvalidCharacters(string handle)
    {
        var act = () => new Channel(UserId, handle, "My Channel", null, Now);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("my-channel")]
    [InlineData("channel123")]
    [InlineData("my-channel-2")]
    public void Constructor_ShouldNotThrow_WhenHandleIsValid(string handle)
    {
        var act = () => new Channel(UserId, handle, "My Channel", null, Now);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_ShouldThrow_WhenNameIsNullOrWhiteSpace(string name)
    {
        var act = () => new Channel(UserId, "my-channel", name, null, Now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenNameExceedsMaxLength()
    {
        var name = new string('a', Channel.NameMaxLength + 1);
        var act = () => new Channel(UserId, "my-channel", name, null, Now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenDescriptionExceedsMaxLength()
    {
        var description = new string('a', Channel.DescriptionMaxLength + 1);
        var act = () => new Channel(UserId, "my-channel", "My Channel", description, Now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UpdateDetails_ShouldThrow_WhenNameExceedsMaxLength()
    {
        var channel = new Channel(UserId, "my-channel", "My Channel", null, Now);
        var name = new string('a', Channel.NameMaxLength + 1);

        var act = () => channel.UpdateDetails(name, null, Now.AddDays(1));

        act.Should().Throw<ArgumentException>();
    }
}
