using Microsoft.EntityFrameworkCore;
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
        ApplyRestrictedDelete(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }

    private static void ApplyRestrictedDelete(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var foreignKey in entityType.GetForeignKeys())
            {
                if (foreignKey.DeleteBehavior != DeleteBehavior.SetNull)
                    foreignKey.DeleteBehavior = DeleteBehavior.Cascade;
            }
        }
    }
}
