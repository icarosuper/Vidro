using FluentAssertions;
using VidroApi.Domain.Entities;

namespace VidroApi.UnitTests.Domain;

public class RefreshTokenTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExpiresAt = Now.AddDays(7);

    [Fact]
    public void Constructor_ShouldSetRequiredProperties()
    {
        var token = new RefreshToken(UserId, "token-value", ExpiresAt, Now);

        token.UserId.Should().Be(UserId);
        token.Token.Should().Be("token-value");
        token.ExpiresAt.Should().Be(ExpiresAt);
        token.IsRevoked.Should().BeFalse();
        token.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public void Constructor_ShouldGenerateUniqueId()
    {
        var token1 = new RefreshToken(UserId, "token-1", ExpiresAt, Now);
        var token2 = new RefreshToken(UserId, "token-2", ExpiresAt, Now);

        token1.Id.Should().NotBe(Guid.Empty);
        token1.Id.Should().NotBe(token2.Id);
    }

    [Fact]
    public void IsExpired_ShouldReturnFalse_WhenBeforeExpiry()
    {
        var token = new RefreshToken(UserId, "token-value", ExpiresAt, Now);

        token.IsExpired(ExpiresAt.AddSeconds(-1)).Should().BeFalse();
    }

    [Fact]
    public void IsExpired_ShouldReturnTrue_WhenAtOrAfterExpiry()
    {
        var token = new RefreshToken(UserId, "token-value", ExpiresAt, Now);

        token.IsExpired(ExpiresAt).Should().BeTrue();
        token.IsExpired(ExpiresAt.AddSeconds(1)).Should().BeTrue();
    }

    [Fact]
    public void IsValid_ShouldReturnTrue_WhenNotRevokedAndNotExpired()
    {
        var token = new RefreshToken(UserId, "token-value", ExpiresAt, Now);

        token.IsValid(Now).Should().BeTrue();
    }

    [Fact]
    public void IsValid_ShouldReturnFalse_WhenExpired()
    {
        var token = new RefreshToken(UserId, "token-value", ExpiresAt, Now);

        token.IsValid(ExpiresAt).Should().BeFalse();
    }

    [Fact]
    public void IsValid_ShouldReturnFalse_WhenRevoked()
    {
        var token = new RefreshToken(UserId, "token-value", ExpiresAt, Now);

        token.Revoke();

        token.IsValid(Now).Should().BeFalse();
    }

    [Fact]
    public void Revoke_ShouldSetIsRevokedToTrue()
    {
        var token = new RefreshToken(UserId, "token-value", ExpiresAt, Now);

        token.Revoke();

        token.IsRevoked.Should().BeTrue();
    }
}
