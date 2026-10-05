namespace VidroApi.Domain.Errors.EntityErrors;

public static partial class Errors
{
    public static class Video
    {
        public static Error NotFound(Guid id) =>
            new("video.not_found", $"Video with id '{id}' was not found.", ErrorType.NotFound);

        public static Error NotOwner() =>
            new("video.not_owner", "You do not own this video.", ErrorType.Forbidden);

        public static Error NotReady() =>
            new("video.not_ready", "The video is not ready yet.", ErrorType.Validation);

        public static Error AlreadyProcessing() =>
            new("video.already_processing", "The video is already being processed.", ErrorType.Conflict);

        public static Error NotPendingUpload() =>
            new("video.not_pending_upload", "The video is not awaiting upload.", ErrorType.Conflict);

        public static Error CannotReactToOwnVideo() =>
            new("video.cannot_react_to_own", "You cannot react to your own video.", ErrorType.Conflict);
    }
}
