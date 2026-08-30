using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VidroApi.Domain.Entities;

namespace VidroApi.Infrastructure.Persistence.Configurations;

public class PlaylistItemConfiguration : IEntityTypeConfiguration<PlaylistItem>
{
    public void Configure(EntityTypeBuilder<PlaylistItem> builder)
    {
        builder.ToTable("playlist_items");

        builder.HasKey(pi => pi.Id);
        builder.Property(pi => pi.Id).HasColumnName("id");
        builder.Property(pi => pi.CreatedAt).HasColumnName("created_at");

        builder.Property(pi => pi.PlaylistId).HasColumnName("playlist_id");
        builder.Property(pi => pi.VideoId).HasColumnName("video_id");

        builder.HasOne(pi => pi.Video)
            .WithMany()
            .HasForeignKey(pi => pi.VideoId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(pi => new { pi.PlaylistId, pi.VideoId }).IsUnique();
        builder.HasIndex(pi => new { pi.PlaylistId, pi.CreatedAt });
        builder.HasIndex(pi => pi.VideoId);
    }
}
