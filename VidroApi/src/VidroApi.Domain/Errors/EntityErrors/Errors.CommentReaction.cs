namespace VidroApi.Domain.Errors.EntityErrors;

public static partial class Errors
{
    public static class CommentReaction
    {
        public static Error NotFound() =>
            new("comment_reaction.not_found", "You have not reacted to this comment.", ErrorType.NotFound);
    }
}
