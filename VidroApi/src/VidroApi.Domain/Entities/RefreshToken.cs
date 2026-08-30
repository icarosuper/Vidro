using System.Diagnostics.CodeAnalysis;

namespace VidroApi.Domain.Entities;

public class RefreshToken : BaseEntity
{
    public const int TokenMaxLength = 256;

    // ReSharper disable once UnusedMember.Local
    [ExcludeFromCodeCoverage]
    private RefreshToken() { }

    public RefreshToken(Guid userId, string token, DateTimeOffset expiresAt, DateTimeOffset now)
        : base(now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        if (token.Length > TokenMaxLength)
            throw new ArgumentException($"Token cannot exceed {TokenMaxLength} characters.", nameof(token));

        UserId = userId;
        Token = token;
        ExpiresAt = expiresAt;
        IsRevoked = false;
    }

    public Guid UserId { get; init; }
    public string Token { get; init; } = null!;
    public DateTimeOffset ExpiresAt { get; init; }
    public bool IsRevoked { get; private set; }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
    public bool IsValid(DateTimeOffset now) => !IsRevoked && !IsExpired(now);

    public void Revoke() => IsRevoked = true;

    // Navigation property
    public User User { get; init; } = null!;
}
