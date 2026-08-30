const errorMessages: Record<string, string> = {
  // Common
  'resource.not_found': 'Resource not found.',
  'request.unauthorized': 'You must be signed in to do this.',
  'request.forbidden': "You don't have permission to do this.",
  'internal.server_error': 'Something went wrong. Please try again.',

  // User
  'user.email_conflict': 'This email address is already in use.',
  'user.username_conflict': 'This username is already taken.',
  'user.incorrect_password': 'The password is incorrect.',
  'user.invalid_credentials': 'Invalid email or password.',

  // Channel
  'channel.not_owner': "You don't own this channel.",
  'channel.limit_reached': "You've reached the maximum number of channels.",
  'channel.cannot_follow_own': "You can't follow your own channel.",
  'channel.already_following': "You're already following this channel.",
  'channel.not_following': "You're not following this channel.",
  'channel.handle_already_in_use': 'This handle is already taken.',

  // Video
  'video.not_found': 'Video not found.',
  'video.not_owner': "You don't own this video.",
  'video.not_ready': "This video isn't ready yet.",
  'video.already_processing': 'This video is already being processed.',
  'video.not_in_processing_state': "This video isn't currently being processed.",
  'video.not_pending_upload': "This video isn't awaiting upload.",
  'video.cannot_react_to_own': "You can't react to your own video.",

  // Comment
  'comment.not_found': 'Comment not found.',
  'comment.not_owner': "You don't own this comment.",
  'comment.already_deleted': 'This comment has already been deleted.',
  'comment.parent_not_found': 'Parent comment not found.',
  'comment.reply_nesting_not_allowed': "You can't reply to a reply.",
  'comment.parent_video_mismatch': "The parent comment belongs to a different video.",

  // Comment reaction
  'comment_reaction.not_found': "You haven't reacted to this comment.",

  // Playlist
  'playlist.not_owner': "You don't own this playlist.",
  'playlist.video_already_in_playlist': 'This video is already in the playlist.',
  'playlist.video_not_in_playlist': "This video isn't in the playlist.",
  'playlist.video_not_from_channel': "This video doesn't belong to this playlist's channel.",
  'playlist.video_ids_mismatch': "The video order doesn't match the current playlist.",

  // Reaction
  'reaction.not_found': "You haven't reacted to this video.",

  // Refresh token
  'refresh_token.not_found': 'Session not found. Please sign in again.',
  'refresh_token.expired': 'Your session has expired. Please sign in again.',
  'refresh_token.revoked': 'Your session was revoked. Please sign in again.',
}

const FALLBACK_MESSAGE = 'An unexpected error occurred.'

export function getApiErrorMessage(error: unknown): string {
  const isApiClientError =
    error instanceof Error && error.name === 'ApiClientError' && 'code' in error

  if (isApiClientError) {
    const code = (error as Error & { code: string }).code
    return errorMessages[code] ?? error.message
  }

  if (error instanceof Error) {
    return error.message
  }

  return FALLBACK_MESSAGE
}
