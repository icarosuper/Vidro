using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VidroApi.Domain.Entities;

namespace VidroApi.Infrastructure.Persistence.Configurations;

public class PendingStorageCleanupConfiguration : IEntityTypeConfiguration<PendingStorageCleanup>
{
    public void Configure(EntityTypeBuilder<PendingStorageCleanup> builder)
    {
        builder.ToTable("pending_storage_cleanups");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at");

        builder.Property(p => p.ObjectPath)
            .HasColumnName("object_path")
            .HasMaxLength(PendingStorageCleanup.ObjectPathMaxLength)
            .IsRequired();

        builder.Property(p => p.IsPrefix).HasColumnName("is_prefix");
    }
}
