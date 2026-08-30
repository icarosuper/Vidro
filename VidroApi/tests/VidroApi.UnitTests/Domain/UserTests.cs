using FluentAssertions;
using VidroApi.Domain.Entities;

namespace VidroApi.UnitTests.Domain;

public class UserTests
{
    [Fact]
    public void Constructor_ShouldSetRequiredProperties()
    {
        var now = DateTimeOffset.UtcNow;
        var user = new User("johndoe", "john@example.com", "hashedpassword", now);

        user.Username.Should().Be("johndoe");
        user.Email.Should().Be("john@example.com");
        user.PasswordHash.Should().Be("hashedpassword");
    }

    [Fact]
    public void Constructor_ShouldGenerateUniqueId()
    {
        var now = DateTimeOffset.UtcNow;
        var user1 = new User("alice", "alice@example.com", "hash1", now);
        var user2 = new User("bob", "bob@example.com", "hash2", now);

        user1.Id.Should().NotBe(Guid.Empty);
        user2.Id.Should().NotBe(Guid.Empty);
        user1.Id.Should().NotBe(user2.Id);
    }

    [Fact]
    public void Constructor_CreatedAtShouldMatchProvidedTime()
    {
        var now = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var user = new User("johndoe", "john@example.com", "hash", now);

        user.CreatedAt.Should().Be(now);
    }

    [Fact]
    public void ChangeEmail_ShouldUpdateEmail()
    {
        var user = new User("johndoe", "john@example.com", "hash", DateTimeOffset.UtcNow);

        user.ChangeEmail("newemail@example.com");

        user.Email.Should().Be("newemail@example.com");
    }

    [Fact]
    public void ChangePasswordHash_ShouldUpdatePasswordHash()
    {
        var user = new User("johndoe", "john@example.com", "oldhash", DateTimeOffset.UtcNow);

        user.ChangePasswordHash("newhash");

        user.PasswordHash.Should().Be("newhash");
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("this_username_is_way_too_long_to_be_valid")]
    public void Constructor_ShouldThrow_WhenUsernameHasInvalidLength(string username)
    {
        var act = () => new User(username, "john@example.com", "hash", DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("john doe")]
    [InlineData("john-doe")]
    [InlineData("john.doe")]
    [InlineData("joão")]
    [InlineData("john@doe")]
    [InlineData("JohnDoe")]
    [InlineData("ALLCAPS")]
    public void Constructor_ShouldThrow_WhenUsernameHasInvalidCharacters(string username)
    {
        var act = () => new User(username, "john@example.com", "hash", DateTimeOffset.UtcNow);

        act.Should().Throw<ArgumentException>().WithMessage("*lowercase letters, digits, and underscores*");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("john_doe")]
    [InlineData("user123")]
    [InlineData("under_score_user")]
    public void Constructor_ShouldAccept_WhenUsernameIsValid(string username)
    {
        var act = () => new User(username, "john@example.com", "hash", DateTimeOffset.UtcNow);

        act.Should().NotThrow();
    }

    // Collection navigation tests added when Channel and RefreshToken are implemented
}
