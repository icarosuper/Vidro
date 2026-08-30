using System.Diagnostics.CodeAnalysis;

namespace VidroApi.Domain.Entities;

public class Comment : BaseAuditableEntity
{
    public const int ContentMaxLength = 1000;

    // ReSharper disable once UnusedMember.Local
    [ExcludeFromCodeCoverage]
    private Comment() { }

    public Comment(Guid videoId, Guid userId, string content, Guid? parentCommentId, DateTimeOffset now)
        : base(now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        if (content.Length > ContentMaxLength)
            throw new ArgumentException($"Content cannot exceed {ContentMaxLength} characters.", nameof(content));

        VideoId = videoId;
        UserId = userId;
        Content = content;
        ParentCommentId = parentCommentId;
        LikeCount = 0;
        DislikeCount = 0;
        ReplyCount = 0;
        IsDeleted = false;
    }

    public Guid VideoId { get; init; }
    public Guid UserId { get; init; }
    public Guid? ParentCommentId { get; init; }
    public string Content { get; private set; } = null!;
    public int LikeCount { get; private set; }
    public int DislikeCount { get; private set; }
    public int ReplyCount { get; private set; }
    public bool IsDeleted { get; private set; }

    public void Edit(string content, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        if (content.Length > ContentMaxLength)
            throw new ArgumentException($"Content cannot exceed {ContentMaxLength} characters.", nameof(content));

        Content = content;
        SetUpdatedAt(now);
    }

    public void SoftDelete(DateTimeOffset now)
    {
        IsDeleted = true;
        SetUpdatedAt(now);
    }

    // Navigation properties
    public Video Video { get; init; } = null!;
    public User User { get; init; } = null!;
    public Comment? ParentComment { get; init; }

    // ReSharper disable once CollectionNeverUpdated.Local
    private readonly List<Comment> _replies = [];
    public IReadOnlyList<Comment> Replies => _replies.AsReadOnly();

    // ReSharper disable once CollectionNeverUpdated.Local
    private readonly List<CommentReaction> _reactions = [];
    public IReadOnlyList<CommentReaction> Reactions => _reactions.AsReadOnly();
}
