using FluentAssertions;
using VidroApi.Domain.Entities;

namespace VidroApi.UnitTests.Domain;

public class CommentValidationTests
{
    private static readonly Guid VideoId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_ShouldThrow_WhenContentIsNullOrWhiteSpace(string content)
    {
        var act = () => new Comment(VideoId, UserId, content, parentCommentId: null, Now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenContentExceedsMaxLength()
    {
        var content = new string('a', Comment.ContentMaxLength + 1);
        var act = () => new Comment(VideoId, UserId, content, parentCommentId: null, Now);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Edit_ShouldThrow_WhenContentIsNullOrWhiteSpace(string content)
    {
        var comment = new Comment(VideoId, UserId, "Valid content", parentCommentId: null, Now);

        var act = () => comment.Edit(content, Now.AddMinutes(1));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Edit_ShouldThrow_WhenContentExceedsMaxLength()
    {
        var comment = new Comment(VideoId, UserId, "Valid content", parentCommentId: null, Now);
        var content = new string('a', Comment.ContentMaxLength + 1);

        var act = () => comment.Edit(content, Now.AddMinutes(1));

        act.Should().Throw<ArgumentException>();
    }
}
