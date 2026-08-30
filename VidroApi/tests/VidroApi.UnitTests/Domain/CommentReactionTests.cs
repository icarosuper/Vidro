using FluentAssertions;
using VidroApi.Domain.Entities;
using VidroApi.Domain.Enums;

namespace VidroApi.UnitTests.Domain;

public class CommentReactionTests
{
    private static readonly Guid CommentId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_ShouldSetRequiredProperties()
    {
        var reaction = new CommentReaction(CommentId, UserId, ReactionType.Like, Now);

        reaction.CommentId.Should().Be(CommentId);
        reaction.UserId.Should().Be(UserId);
        reaction.Type.Should().Be(ReactionType.Like);
        reaction.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public void ChangeType_ShouldUpdateType()
    {
        var reaction = new CommentReaction(CommentId, UserId, ReactionType.Like, Now);

        reaction.ChangeType(ReactionType.Dislike);

        reaction.Type.Should().Be(ReactionType.Dislike);
    }
}
