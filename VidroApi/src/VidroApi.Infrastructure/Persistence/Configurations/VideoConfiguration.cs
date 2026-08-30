using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VidroApi.Domain.Entities;

namespace VidroApi.Infrastructure.Persistence.Configurations;

public class VideoConfiguration : IEntityTypeConfiguration<Video>
{
    public void Configure(EntityTypeBuilder<Video> builder)
    {
        builder.ToTable("videos");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).HasColumnName("id");
        builder.Property(v => v.CreatedAt).HasColumnName("created_at");
        builder.Property(v => v.UpdatedAt).HasColumnName("updated_at");

        builder.Property(v => v.ChannelId).HasColumnName("channel_id");

        builder.Property(v => v.Title)
            .HasColumnName("title")
            .HasMaxLength(Video.TitleMaxLength)
            .IsRequired();

        builder.Property(v => v.Description)
            .HasColumnName("description")
            .HasMaxLength(Video.DescriptionMaxLength);

        builder.Property(v => v.Tags)
            .HasColumnName("tags")
            .HasColumnType("text[]");

        builder.Property(v => v.Visibility).HasColumnName("visibility");
        builder.Property(v => v.Status).HasColumnName("status");
        builder.Property(v => v.UploadExpiresAt).HasColumnName("upload_expires_at");
        builder.Property(v => v.ViewCount).HasColumnName("view_count");
        builder.Property(v => v.LikeCount).HasColumnName("like_count");
        builder.Property(v => v.DislikeCount).HasColumnName("dislike_count");

        builder.HasOne(v => v.Channel)
            .WithMany()
            .HasForeignKey(v => v.ChannelId);

        builder.HasOne(v => v.Artifacts)
            .WithOne(a => a.Video)
            .HasForeignKey<VideoArtifacts>(a => a.VideoId);

        builder.HasOne(v => v.Metadata)
            .WithOne(m => m.Video)
            .HasForeignKey<VideoMetadata>(m => m.VideoId);

        builder.HasIndex(v => new { v.Status, v.Visibility });
    }
}
