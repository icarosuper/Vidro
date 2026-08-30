using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VidroApi.Domain.Entities;

namespace VidroApi.Infrastructure.Persistence.Configurations;

public class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.ToTable("comments");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");

        builder.Property(c => c.VideoId).HasColumnName("video_id");
        builder.Property(c => c.UserId).HasColumnName("user_id");
        builder.Property(c => c.ParentCommentId).HasColumnName("parent_comment_id");

        builder.Property(c => c.Content)
            .HasColumnName("content")
            .HasMaxLength(Comment.ContentMaxLength)
            .IsRequired();

        builder.Property(c => c.LikeCount).HasColumnName("like_count");
        builder.Property(c => c.DislikeCount).HasColumnName("dislike_count");
        builder.Property(c => c.ReplyCount).HasColumnName("reply_count");
        builder.Property(c => c.IsDeleted).HasColumnName("is_deleted");

        builder.HasOne(c => c.Video)
            .WithMany(v => v.Comments)
            .HasForeignKey(c => c.VideoId);

        builder.HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId);

        builder.HasOne(c => c.ParentComment)
            .WithMany(p => p.Replies)
            .HasForeignKey(c => c.ParentCommentId);

        builder.HasMany(c => c.Reactions)
            .WithOne(r => r.Comment)
            .HasForeignKey(r => r.CommentId);

        builder.HasIndex(c => new { c.VideoId, c.ParentCommentId });
        builder.HasIndex(c => new { c.ParentCommentId, c.CreatedAt });
    }
}
