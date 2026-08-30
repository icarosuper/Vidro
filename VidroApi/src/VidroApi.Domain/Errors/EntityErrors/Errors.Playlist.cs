namespace VidroApi.Domain.Errors.EntityErrors;

public static partial class Errors
{
    public static class Playlist
    {
        public static Error NotOwner() =>
            new("playlist.not_owner", "You do not own this playlist.", ErrorType.Forbidden);

        public static Error VideoAlreadyInPlaylist() =>
            new("playlist.video_already_in_playlist", "This video is already in the playlist.", ErrorType.Conflict);

        public static Error VideoNotInPlaylist() =>
            new("playlist.video_not_in_playlist", "This video is not in the playlist.", ErrorType.NotFound);

        public static Error VideoNotFromChannel() =>
            new("playlist.video_not_from_channel", "This video does not belong to the playlist's channel.", ErrorType.Validation);

        public static Error VideoIdsMismatch() =>
            new("playlist.video_ids_mismatch", "The provided video IDs do not match the playlist's current items.", ErrorType.Validation);
    }
}
