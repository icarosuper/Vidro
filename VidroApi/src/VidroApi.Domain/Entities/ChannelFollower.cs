using System.Diagnostics.CodeAnalysis;

namespace VidroApi.Domain.Entities;

public class ChannelFollower : BaseEntity
{
    // ReSharper disable once UnusedMember.Local
    [ExcludeFromCodeCoverage]
    private ChannelFollower() { }

    public ChannelFollower(Guid channelId, Guid userId, DateTimeOffset now)
        : base(now)
    {
        ChannelId = channelId;
        UserId = userId;
    }

    public Guid ChannelId { get; init; }
    public Guid UserId { get; init; }

    // Navigation properties
    public Channel Channel { get; init; } = null!;
    public User User { get; init; } = null!;
}
