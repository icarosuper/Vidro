using System.Diagnostics.CodeAnalysis;
using VidroApi.Domain.Enums;

namespace VidroApi.Domain.Entities;

public class CommentReaction : BaseEntity
{
    // ReSharper disable once UnusedMember.Local
    [ExcludeFromCodeCoverage]
    private CommentReaction() { }

    public CommentReaction(Guid commentId, Guid userId, ReactionType type, DateTimeOffset now)
        : base(now)
    {
        CommentId = commentId;
        UserId = userId;
        Type = type;
    }

    public Guid CommentId { get; init; }
    public Guid UserId { get; init; }
    public ReactionType Type { get; private set; }

    public void ChangeType(ReactionType type) => Type = type;

    // Navigation properties
    public Comment Comment { get; init; } = null!;
    public User User { get; init; } = null!;
}
