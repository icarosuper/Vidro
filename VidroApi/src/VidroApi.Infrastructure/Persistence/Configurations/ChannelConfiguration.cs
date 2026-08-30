using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VidroApi.Domain.Entities;

namespace VidroApi.Infrastructure.Persistence.Configurations;

public class ChannelConfiguration : IEntityTypeConfiguration<Channel>
{
    public void Configure(EntityTypeBuilder<Channel> builder)
    {
        builder.ToTable("channels");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");

        builder.Property(c => c.UserId).HasColumnName("user_id");

        builder.Property(c => c.Handle)
            .HasColumnName("handle")
            .HasMaxLength(Channel.HandleMaxLength)
            .IsRequired();

        builder.HasIndex(c => new { c.UserId, c.Handle }).IsUnique();

        builder.Property(c => c.Name)
            .HasColumnName("name")
            .HasMaxLength(Channel.NameMaxLength)
            .IsRequired();

        builder.Property(c => c.Description)
            .HasColumnName("description")
            .HasMaxLength(Channel.DescriptionMaxLength);

        builder.Property(c => c.FollowerCount).HasColumnName("follower_count");

        builder.HasOne(c => c.User)
            .WithMany(u => u.Channels)
            .HasForeignKey(c => c.UserId);
    }
}
