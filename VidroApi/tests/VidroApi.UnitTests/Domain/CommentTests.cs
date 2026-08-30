using FluentAssertions;
using VidroApi.Domain.Entities;

namespace VidroApi.UnitTests.Domain;

public class CommentTests
{
    private static readonly Guid VideoId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_ShouldSetRequiredProperties()
    {
        var comment = new Comment(VideoId, UserId, "Great video!", parentCommentId: null, Now);

        comment.VideoId.Should().Be(VideoId);
        comment.UserId.Should().Be(UserId);
        comment.Content.Should().Be("Great video!");
        comment.ParentCommentId.Should().BeNull();
        comment.LikeCount.Should().Be(0);
        comment.DislikeCount.Should().Be(0);
        comment.ReplyCount.Should().Be(0);
        comment.IsDeleted.Should().BeFalse();
        comment.CreatedAt.Should().Be(Now);
        comment.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void Constructor_ShouldSetParentCommentId_WhenProvided()
    {
        var parentId = Guid.NewGuid();
        var reply = new Comment(VideoId, UserId, "Reply content", parentCommentId: parentId, Now);

        reply.ParentCommentId.Should().Be(parentId);
    }

    [Fact]
    public void Edit_ShouldUpdateContentAndSetUpdatedAt()
    {
        var comment = new Comment(VideoId, UserId, "Original content", parentCommentId: null, Now);
        var editedAt = Now.AddMinutes(10);

        comment.Edit("Edited content", editedAt);

        comment.Content.Should().Be("Edited content");
        comment.UpdatedAt.Should().Be(editedAt);
    }

    [Fact]
    public void Delete_ShouldSetIsDeletedAndSetUpdatedAt()
    {
        var comment = new Comment(VideoId, UserId, "Some content", parentCommentId: null, Now);
        var deletedAt = Now.AddMinutes(5);

        comment.SoftDelete(deletedAt);

        comment.IsDeleted.Should().BeTrue();
        comment.UpdatedAt.Should().Be(deletedAt);
    }
}
