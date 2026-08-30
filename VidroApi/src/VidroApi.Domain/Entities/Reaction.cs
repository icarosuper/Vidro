using System.Diagnostics.CodeAnalysis;
using VidroApi.Domain.Enums;

namespace VidroApi.Domain.Entities;

public class Reaction : BaseEntity
{
    // ReSharper disable once UnusedMember.Local
    [ExcludeFromCodeCoverage]
    private Reaction() { }

    public Reaction(Guid videoId, Guid userId, ReactionType type, DateTimeOffset now)
        : base(now)
    {
        VideoId = videoId;
        UserId = userId;
        Type = type;
    }

    public Guid VideoId { get; init; }
    public Guid UserId { get; init; }
    public ReactionType Type { get; private set; }

    public void ChangeType(ReactionType type) => Type = type;

    // Navigation properties
    public Video Video { get; init; } = null!;
    public User User { get; init; } = null!;
}
