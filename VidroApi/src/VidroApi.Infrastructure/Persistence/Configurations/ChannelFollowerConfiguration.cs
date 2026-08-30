using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VidroApi.Domain.Entities;

namespace VidroApi.Infrastructure.Persistence.Configurations;

public class ChannelFollowerConfiguration : IEntityTypeConfiguration<ChannelFollower>
{
    public void Configure(EntityTypeBuilder<ChannelFollower> builder)
    {
        builder.ToTable("channel_followers");

        builder.HasKey(cf => cf.Id);
        builder.Property(cf => cf.Id).HasColumnName("id");
        builder.Property(cf => cf.CreatedAt).HasColumnName("created_at");

        builder.Property(cf => cf.ChannelId).HasColumnName("channel_id");
        builder.Property(cf => cf.UserId).HasColumnName("user_id");

        builder.HasIndex(cf => new { cf.ChannelId, cf.UserId }).IsUnique();
        builder.HasIndex(cf => cf.UserId);

        builder.HasOne(cf => cf.Channel)
            .WithMany()
            .HasForeignKey(cf => cf.ChannelId);

        builder.HasOne(cf => cf.User)
            .WithMany()
            .HasForeignKey(cf => cf.UserId);
    }
}
