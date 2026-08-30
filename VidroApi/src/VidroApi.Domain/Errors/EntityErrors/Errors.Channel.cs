namespace VidroApi.Domain.Errors.EntityErrors;

public static partial class Errors
{
    public static class Channel
    {
        public static Error NotOwner() =>
            new("channel.not_owner", "You do not own this channel.", ErrorType.Forbidden);

        public static Error LimitReached(int limit) =>
            new("channel.limit_reached", $"You cannot have more than {limit} channels.", ErrorType.Conflict);

        public static Error CannotFollowOwnChannel() =>
            new("channel.cannot_follow_own", "You cannot follow your own channel.", ErrorType.Conflict);

        public static Error AlreadyFollowing() =>
            new("channel.already_following", "You are already following this channel.", ErrorType.Conflict);

        public static Error NotFollowing() =>
            new("channel.not_following", "You are not following this channel.", ErrorType.NotFound);

        public static Error HandleAlreadyInUse() =>
            new("channel.handle_already_in_use", "You already have a channel with this handle.", ErrorType.Conflict);
    }
}
