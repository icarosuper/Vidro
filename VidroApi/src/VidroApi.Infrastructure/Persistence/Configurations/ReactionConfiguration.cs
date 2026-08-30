using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VidroApi.Domain.Entities;

namespace VidroApi.Infrastructure.Persistence.Configurations;

public class ReactionConfiguration : IEntityTypeConfiguration<Reaction>
{
    public void Configure(EntityTypeBuilder<Reaction> builder)
    {
        builder.ToTable("reactions");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");

        builder.Property(r => r.VideoId).HasColumnName("video_id");
        builder.Property(r => r.UserId).HasColumnName("user_id");
        builder.Property(r => r.Type).HasColumnName("type");

        builder.HasIndex(r => new { r.VideoId, r.UserId }).IsUnique();

        builder.HasOne(r => r.Video)
            .WithMany(v => v.Reactions)
            .HasForeignKey(r => r.VideoId);

        builder.HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId);
    }
}
