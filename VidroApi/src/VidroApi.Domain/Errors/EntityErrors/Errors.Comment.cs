namespace VidroApi.Domain.Errors.EntityErrors;

public static partial class Errors
{
    public static class Comment
    {
        public static Error NotFound(Guid id) =>
            new("comment.not_found", $"Comment with id '{id}' was not found.", ErrorType.NotFound);

        public static Error NotOwner() =>
            new("comment.not_owner", "You do not own this comment.", ErrorType.Forbidden);

        public static Error AlreadyDeleted() =>
            new("comment.already_deleted", "The comment has already been deleted.", ErrorType.Conflict);

        public static Error ParentNotFound(Guid id) =>
            new("comment.parent_not_found", $"Parent comment with id '{id}' was not found.", ErrorType.NotFound);

        public static Error ReplyNestingNotAllowed() =>
            new("comment.reply_nesting_not_allowed", "Replies to replies are not allowed.", ErrorType.Validation);

        public static Error ParentVideoMismatch() =>
            new("comment.parent_video_mismatch", "The parent comment does not belong to this video.", ErrorType.Validation);
    }
}
