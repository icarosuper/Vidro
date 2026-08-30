using FluentAssertions;
using VidroApi.Domain.Entities;

namespace VidroApi.UnitTests.Domain;

public class UserValidationTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_ShouldThrow_WhenUsernameIsNullOrWhiteSpace(string username)
    {
        var act = () => new User(username, "john@example.com", "hash", Now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenUsernameExceedsMaxLength()
    {
        var username = new string('a', User.UsernameMaxLength + 1);
        var act = () => new User(username, "john@example.com", "hash", Now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenEmailExceedsMaxLength()
    {
        var email = new string('a', User.EmailMaxLength + 1);
        var act = () => new User("john", email, "hash", Now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ChangeEmail_ShouldThrow_WhenEmailExceedsMaxLength()
    {
        var user = new User("john", "john@example.com", "hash", Now);
        var email = new string('a', User.EmailMaxLength + 1);

        var act = () => user.ChangeEmail(email);

        act.Should().Throw<ArgumentException>();
    }
}
