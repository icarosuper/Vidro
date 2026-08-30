using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace VidroApi.Domain.Entities;

public class Channel : BaseAuditableEntity
{
    public const int HandleMinLength = 3;
    public const int HandleMaxLength = 50;
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 500;

    private static readonly Regex HandlePattern = new(@"^[a-z0-9-]+$", RegexOptions.Compiled);

    // ReSharper disable once UnusedMember.Local
    [ExcludeFromCodeCoverage]
    private Channel() { }

    public Channel(Guid userId, string handle, string name, string? description, DateTimeOffset now)
        : base(now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(handle);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (handle.Length < HandleMinLength || handle.Length > HandleMaxLength)
            throw new ArgumentException($"Handle must be between {HandleMinLength} and {HandleMaxLength} characters.", nameof(handle));
        if (!HandlePattern.IsMatch(handle))
            throw new ArgumentException("Handle may only contain lowercase letters, digits, and hyphens.", nameof(handle));
        if (name.Length > NameMaxLength)
            throw new ArgumentException($"Name cannot exceed {NameMaxLength} characters.", nameof(name));
        if (description is { Length: > DescriptionMaxLength })
            throw new ArgumentException($"Description cannot exceed {DescriptionMaxLength} characters.", nameof(description));

        UserId = userId;
        Handle = handle;
        Name = name;
        Description = description;
        FollowerCount = 0;
    }

    public Guid UserId { get; init; }
    public string Handle { get; init; } = null!;
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public int FollowerCount { get; private set; }
    public string? AvatarPath { get; private set; }

    public void UpdateDetails(string name, string? description, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > NameMaxLength)
            throw new ArgumentException($"Name cannot exceed {NameMaxLength} characters.", nameof(name));
        if (description is { Length: > DescriptionMaxLength })
            throw new ArgumentException($"Description cannot exceed {DescriptionMaxLength} characters.", nameof(description));

        Name = name;
        Description = description;
        SetUpdatedAt(now);
    }

    public void SetAvatar(string path, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        AvatarPath = path;
        SetUpdatedAt(now);
    }

    // Navigation properties
    public User User { get; init; } = null!;

    // private List<Video> _videos = [];
    // public IReadOnlyList<Video> Videos => _videos.AsReadOnly();
}
