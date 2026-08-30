using System.Diagnostics.CodeAnalysis;
using VidroApi.Domain.Enums;

namespace VidroApi.Domain.Entities;

public class Playlist : BaseAuditableEntity
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 500;

    // ReSharper disable once UnusedMember.Local
    [ExcludeFromCodeCoverage]
    private Playlist() { }

    public Playlist(Guid userId, Guid? channelId, string name, string? description,
        PlaylistScope scope, PlaylistVisibility visibility, DateTimeOffset now)
        : base(now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > NameMaxLength)
            throw new ArgumentException($"Name cannot exceed {NameMaxLength} characters.", nameof(name));
        if (description is { Length: > DescriptionMaxLength })
            throw new ArgumentException($"Description cannot exceed {DescriptionMaxLength} characters.", nameof(description));

        UserId = userId;
        ChannelId = channelId;
        Name = name;
        Description = description;
        Scope = scope;
        Visibility = visibility;
        VideoCount = 0;
    }

    public Guid UserId { get; init; }
    public Guid? ChannelId { get; init; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public PlaylistScope Scope { get; init; }
    public PlaylistVisibility Visibility { get; private set; }
    public int VideoCount { get; private set; }

    public bool IsPrivate => Visibility == PlaylistVisibility.Private;

    public void UpdateDetails(string name, string? description, PlaylistVisibility visibility, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > NameMaxLength)
            throw new ArgumentException($"Name cannot exceed {NameMaxLength} characters.", nameof(name));
        if (description is { Length: > DescriptionMaxLength })
            throw new ArgumentException($"Description cannot exceed {DescriptionMaxLength} characters.", nameof(description));

        Name = name;
        Description = description;
        Visibility = visibility;
        SetUpdatedAt(now);
    }

    // Navigation properties
    public User User { get; init; } = null!;
    public Channel? Channel { get; init; }

    // ReSharper disable once CollectionNeverUpdated.Local
    private readonly List<PlaylistItem> _items = [];
    public IReadOnlyList<PlaylistItem> Items => _items.AsReadOnly();
}
