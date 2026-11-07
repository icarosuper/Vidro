using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VidroApi.Domain.Entities;
using VidroApi.Infrastructure.Persistence;

namespace VidroApi.UnitTests.Persistence;

// design-decisions.md #8: Cascade is the default, and an FK that sets its own delete behavior on
// purpose keeps it. Building the model needs the provider but no database.
public class AppDbContextTests
{
    private static DbContextOptions<AppDbContext> ModelOnlyOptions() =>
        new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=unused").Options;

    [Fact]
    public void Model_EveryForeignKeyCascades()
    {
        using var db = new AppDbContext(ModelOnlyOptions());

        var foreignKeys = db.Model.GetEntityTypes().SelectMany(entityType => entityType.GetForeignKeys());

        foreignKeys.Should().AllSatisfy(foreignKey =>
            foreignKey.DeleteBehavior.Should().Be(DeleteBehavior.Cascade,
                $"{foreignKey.DeclaringEntityType.ClrType.Name}.{string.Join(",", foreignKey.Properties.Select(p => p.Name))} sets no behavior of its own"));
    }

    [Fact]
    public void Model_ForeignKeyConfiguredRestrict_KeepsRestrict()
    {
        using var db = new ContextWithRestrictedPlaylistItems(ModelOnlyOptions());

        var playlistForeignKey = db.Model.FindEntityType(typeof(PlaylistItem))!
            .GetForeignKeys()
            .Single(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(Playlist));

        playlistForeignKey.DeleteBehavior.Should().Be(DeleteBehavior.Restrict,
            "an explicit OnDelete is a deliberate exception, not something the default overwrites");
    }

    private class ContextWithRestrictedPlaylistItems(DbContextOptions<AppDbContext> options)
        : AppDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PlaylistItem>()
                .HasOne(item => item.Playlist)
                .WithMany(playlist => playlist.Items)
                .HasForeignKey(item => item.PlaylistId)
                .OnDelete(DeleteBehavior.Restrict);
            base.OnModelCreating(modelBuilder);
        }
    }
}
