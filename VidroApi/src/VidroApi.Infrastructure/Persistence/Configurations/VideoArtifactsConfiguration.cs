using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VidroApi.Domain.Entities;

namespace VidroApi.Infrastructure.Persistence.Configurations;

public class VideoArtifactsConfiguration : IEntityTypeConfiguration<VideoArtifacts>
{
    public void Configure(EntityTypeBuilder<VideoArtifacts> builder)
    {
        builder.ToTable("video_artifacts");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at");

        builder.Property(a => a.VideoId).HasColumnName("video_id");

        builder.Property(a => a.ProcessedPath)
            .HasColumnName("processed_path")
            .HasMaxLength(VideoArtifacts.PathMaxLength)
            .IsRequired();

        builder.Property(a => a.PreviewPath)
            .HasColumnName("preview_path")
            .HasMaxLength(VideoArtifacts.PathMaxLength)
            .IsRequired(false);

        builder.Property(a => a.HlsPath)
            .HasColumnName("hls_path")
            .HasMaxLength(VideoArtifacts.PathMaxLength)
            .IsRequired(false);

        builder.Property(a => a.AudioPath)
            .HasColumnName("audio_path")
            .HasMaxLength(VideoArtifacts.PathMaxLength)
            .IsRequired(false);

        builder.Property(a => a.ThumbnailPaths)
            .HasColumnName("thumbnail_paths")
            .HasColumnType("text[]");

        builder.Property(a => a.CustomThumbnailPath)
            .HasColumnName("custom_thumbnail_path")
            .HasMaxLength(VideoArtifacts.PathMaxLength)
            .IsRequired(false);
    }
}
