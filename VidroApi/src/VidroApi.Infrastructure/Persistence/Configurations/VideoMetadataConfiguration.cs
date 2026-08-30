using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VidroApi.Domain.Entities;

namespace VidroApi.Infrastructure.Persistence.Configurations;

public class VideoMetadataConfiguration : IEntityTypeConfiguration<VideoMetadata>
{
    public void Configure(EntityTypeBuilder<VideoMetadata> builder)
    {
        builder.ToTable("video_metadata");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id");
        builder.Property(m => m.CreatedAt).HasColumnName("created_at");

        builder.Property(m => m.VideoId).HasColumnName("video_id");
        builder.Property(m => m.FileSizeBytes).HasColumnName("file_size_bytes");
        builder.Property(m => m.DurationSeconds).HasColumnName("duration_seconds");
        builder.Property(m => m.Width).HasColumnName("width");
        builder.Property(m => m.Height).HasColumnName("height");

        builder.Property(m => m.Codec)
            .HasColumnName("codec")
            .HasMaxLength(VideoMetadata.CodecMaxLength)
            .IsRequired();
    }
}
