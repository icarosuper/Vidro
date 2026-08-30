using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace VidroApi.Domain.Entities;

public class User : BaseEntity
{
    public const int UsernameMinLength = 3;
    public const int UsernameMaxLength = 25;
    public const int EmailMaxLength = 255;
    public const int PasswordMinLength = 8;

    private static readonly Regex UsernamePattern = new(@"^[a-z0-9_]+$", RegexOptions.Compiled);

    // ReSharper disable once UnusedMember.Local
    [ExcludeFromCodeCoverage]
    private User() { }

    public User(string username, string email, string passwordHash, DateTimeOffset now)
        : base(now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        if (username.Length < UsernameMinLength || username.Length > UsernameMaxLength)
            throw new ArgumentException($"Username must be between {UsernameMinLength} and {UsernameMaxLength} characters.", nameof(username));
        if (!UsernamePattern.IsMatch(username))
            throw new ArgumentException("Username may only contain lowercase letters, digits, and underscores.", nameof(username));
        if (email.Length > EmailMaxLength)
            throw new ArgumentException($"Email cannot exceed {EmailMaxLength} characters.", nameof(email));

        Username = username;
        Email = email;
        PasswordHash = passwordHash;
    }

    public string Username { get; init; } = null!;
    public string Email { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;

    public void ChangeEmail(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        if (email.Length > EmailMaxLength)
            throw new ArgumentException($"Email cannot exceed {EmailMaxLength} characters.", nameof(email));
        Email = email;
    }

    public void ChangePasswordHash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
    }

    public string? AvatarPath { get; private set; }

    public void SetAvatar(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        AvatarPath = path;
    }

    // ReSharper disable once CollectionNeverUpdated.Local
    private readonly List<RefreshToken> _refreshTokens = [];
    public IReadOnlyList<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();

    // ReSharper disable once CollectionNeverUpdated.Local
    private readonly List<Channel> _channels = [];
    public IReadOnlyList<Channel> Channels => _channels.AsReadOnly();
}
