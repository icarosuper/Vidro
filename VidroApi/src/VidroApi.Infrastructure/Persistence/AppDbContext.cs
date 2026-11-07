using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using VidroApi.Domain.Entities;

namespace VidroApi.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<ChannelFollower> ChannelFollowers => Set<ChannelFollower>();
    public DbSet<Video> Videos => Set<Video>();
    public DbSet<VideoArtifacts> VideoArtifacts => Set<VideoArtifacts>();
    public DbSet<VideoMetadata> VideoMetadata => Set<VideoMetadata>();
    public DbSet<Reaction> Reactions => Set<Reaction>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<CommentReaction> CommentReactions => Set<CommentReaction>();
    public DbSet<PendingStorageCleanup> PendingStorageCleanups => Set<PendingStorageCleanup>();
    public DbSet<Playlist> Playlists => Set<Playlist>();
    public DbSet<PlaylistItem> PlaylistItems => Set<PlaylistItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        ApplyCascadeDeleteByDefault(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }

    // Cascade on every FK (design-decisions.md #8), except one whose configuration calls OnDelete
    // on purpose — Restrict to block a delete, SetNull to let the child survive. Only those carry an
    // Explicit configuration source; everything else is EF's convention, which this replaces.
    private static void ApplyCascadeDeleteByDefault(ModelBuilder modelBuilder)
    {
        var foreignKeys = modelBuilder.Model.GetEntityTypes().SelectMany(entityType => entityType.GetForeignKeys());
        foreach (var foreignKey in foreignKeys)
        {
            var deleteBehaviorSource = ((IConventionForeignKey)foreignKey).GetDeleteBehaviorConfigurationSource();
            var configuredOnPurpose = deleteBehaviorSource == ConfigurationSource.Explicit;
            if (configuredOnPurpose)
                continue;

            foreignKey.DeleteBehavior = DeleteBehavior.Cascade;
        }
    }
}
