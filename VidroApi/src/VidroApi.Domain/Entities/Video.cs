using System.Diagnostics.CodeAnalysis;
using VidroApi.Domain.Enums;

namespace VidroApi.Domain.Entities;

public class Video : BaseAuditableEntity
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 2000;

    // ReSharper disable once UnusedMember.Local
    [ExcludeFromCodeCoverage]
    private Video() { }

    public Video(Guid channelId, string title, string? description, List<string> tags,
        VideoVisibility visibility, DateTimeOffset uploadExpiresAt, DateTimeOffset now)
        : base(now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(tags);
        if (title.Length > TitleMaxLength)
            throw new ArgumentException($"Title cannot exceed {TitleMaxLength} characters.", nameof(title));
        if (description is { Length: > DescriptionMaxLength })
            throw new ArgumentException($"Description cannot exceed {DescriptionMaxLength} characters.", nameof(description));

        ChannelId = channelId;
        Title = title;
        Description = description;
        Tags = tags;
        Visibility = visibility;
        Status = VideoStatus.PendingUpload;
        UploadExpiresAt = uploadExpiresAt;
        ViewCount = 0;
        LikeCount = 0;
        DislikeCount = 0;
        CommentCount = 0;
    }

    public Guid ChannelId { get; init; }
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public List<string> Tags { get; private set; } = null!;
    public VideoVisibility Visibility { get; private set; }
    public VideoStatus Status { get; private set; }
    public DateTimeOffset UploadExpiresAt { get; private set; }
    public int ViewCount { get; private set; }
    public int LikeCount { get; private set; }
    public int DislikeCount { get; private set; }
    public int CommentCount { get; private set; }
    
    public bool IsPrivate => Visibility == VideoVisibility.Private;

    public void UpdateDetails(string title, string? description, List<string> tags,
        VideoVisibility visibility, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(tags);
        if (title.Length > TitleMaxLength)
            throw new ArgumentException($"Title cannot exceed {TitleMaxLength} characters.", nameof(title));
        if (description is { Length: > DescriptionMaxLength })
            throw new ArgumentException($"Description cannot exceed {DescriptionMaxLength} characters.", nameof(description));

        Title = title;
        Description = description;
        Tags = tags;
        Visibility = visibility;
        SetUpdatedAt(now);
    }

    public void ChangeVisibility(VideoVisibility visibility, DateTimeOffset now)
    {
        Visibility = visibility;
        SetUpdatedAt(now);
    }

    public void MarkAsProcessing(DateTimeOffset now)
    {
        Status = VideoStatus.Processing;
        SetUpdatedAt(now);
    }

    public void MarkAsReady(DateTimeOffset now)
    {
        Status = VideoStatus.Ready;
        SetUpdatedAt(now);
    }

    public void MarkAsFailed(DateTimeOffset now)
    {
        Status = VideoStatus.Failed;
        SetUpdatedAt(now);
    }

    // Navigation properties
    public Channel Channel { get; init; } = null!;
    public VideoArtifacts? Artifacts { get; private set; }
    public VideoMetadata? Metadata { get; private set; }

    // ReSharper disable once CollectionNeverUpdated.Local
    private readonly List<Reaction> _reactions = [];
    public IReadOnlyList<Reaction> Reactions => _reactions.AsReadOnly();

    // ReSharper disable once CollectionNeverUpdated.Local
    private readonly List<Comment> _comments = [];
    public IReadOnlyList<Comment> Comments => _comments.AsReadOnly();
}
