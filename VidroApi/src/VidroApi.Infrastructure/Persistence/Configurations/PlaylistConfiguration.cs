using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VidroApi.Domain.Entities;

namespace VidroApi.Infrastructure.Persistence.Configurations;

public class PlaylistConfiguration : IEntityTypeConfiguration<Playlist>
{
    public void Configure(EntityTypeBuilder<Playlist> builder)
    {
        builder.ToTable("playlists");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at");

        builder.Property(p => p.UserId).HasColumnName("user_id");
        builder.Property(p => p.ChannelId).HasColumnName("channel_id");

        builder.Property(p => p.Name)
            .HasColumnName("name")
            .HasMaxLength(Playlist.NameMaxLength)
            .IsRequired();

        builder.Property(p => p.Description)
            .HasColumnName("description")
            .HasMaxLength(Playlist.DescriptionMaxLength);

        builder.Property(p => p.Scope).HasColumnName("scope");
        builder.Property(p => p.Visibility).HasColumnName("visibility");
        builder.Property(p => p.VideoCount).HasColumnName("video_count");

        builder.HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId);

        builder.HasOne(p => p.Channel)
            .WithMany()
            .HasForeignKey(p => p.ChannelId);

        builder.HasMany(p => p.Items)
            .WithOne(pi => pi.Playlist)
            .HasForeignKey(pi => pi.PlaylistId);

        builder.HasIndex(p => p.UserId);
        builder.HasIndex(p => p.ChannelId);
    }
}
