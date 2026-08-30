using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VidroApi.Domain.Entities;

namespace VidroApi.Infrastructure.Persistence.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");

        builder.Property(r => r.UserId).HasColumnName("user_id");

        builder.Property(r => r.Token)
            .HasColumnName("token")
            .HasMaxLength(RefreshToken.TokenMaxLength)
            .IsRequired();

        builder.Property(r => r.ExpiresAt).HasColumnName("expires_at");
        builder.Property(r => r.IsRevoked).HasColumnName("is_revoked");

        builder.HasIndex(r => r.Token).IsUnique();

        builder.HasOne(r => r.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(r => r.UserId);
    }
}
