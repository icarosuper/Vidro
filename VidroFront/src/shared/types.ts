// Wrapper de sucesso para todas as respostas da API
export type ApiSuccess<T> = { data: T }

// Erro de domínio (400/401/403/404/409)
export type ApiError = {
  code: string
  message: string
}

// Erro de validação (400 — FluentValidation)
export type ValidationError = {
  errors: Array<{ field: string; message: string }>
}

// Toda enum na resposta da API vem como EnumValue
// Nos requests, enviar apenas o id (número inteiro)
export type EnumValue = {
  id: number
  value: string
}

// Página paginada por cursor — estilo usado em vídeos, comentários e replies
// K é o nome do campo do array (ex: 'videos', 'comments', 'replies')
export type CursorPage<K extends string, T> = {
  [key in K]: T[]
} & {
  nextCursor: string | null
}

// Página paginada por cursor — estilo usado em playlists (PagedResult<T>)
export type PagedResult<T> = {
  items: T[]
  nextCursor: string | null
}

// Códigos de erro conhecidos da API
export const API_ERROR_CODES = {
  UNAUTHORIZED: 'request.unauthorized',
  FORBIDDEN: 'request.forbidden',
  NOT_FOUND: 'resource.not_found',
  EMAIL_CONFLICT: 'user.email_conflict',
  USERNAME_CONFLICT: 'user.username_conflict',
  INVALID_CREDENTIALS: 'user.invalid_credentials',
  REFRESH_TOKEN_NOT_FOUND: 'refresh_token.not_found',
  REFRESH_TOKEN_EXPIRED: 'refresh_token.expired',
  REFRESH_TOKEN_REVOKED: 'refresh_token.revoked',
  CHANNEL_NOT_OWNER: 'channel.not_owner',
  CHANNEL_LIMIT_REACHED: 'channel.limit_reached',
  CHANNEL_CANNOT_FOLLOW_OWN: 'channel.cannot_follow_own',
  CHANNEL_ALREADY_FOLLOWING: 'channel.already_following',
  CHANNEL_NOT_FOLLOWING: 'channel.not_following',
  VIDEO_NOT_OWNER: 'video.not_owner',
  VIDEO_CANNOT_REACT_OWN: 'video.cannot_react_to_own',
  COMMENT_NOT_OWNER: 'comment.not_owner',
  COMMENT_REPLY_NESTING: 'comment.reply_nesting_not_allowed',
  PLAYLIST_NOT_OWNER: 'playlist.not_owner',
  PLAYLIST_VIDEO_ALREADY_IN: 'playlist.video_already_in_playlist',
  PLAYLIST_VIDEO_NOT_IN: 'playlist.video_not_in_playlist',
} as const

export type ApiErrorCode = (typeof API_ERROR_CODES)[keyof typeof API_ERROR_CODES]

// Enums espelhando os da API
export const VideoStatus = { PendingUpload: 0, Processing: 1, Ready: 2, Failed: 3 } as const
export const VideoVisibility = { Public: 0, Unlisted: 1, Private: 2 } as const
export const ReactionType = { Like: 1, Dislike: 2 } as const
export const PlaylistVisibility = { Public: 0, Private: 1 } as const
export const PlaylistScope = { User: 0, Channel: 1 } as const
export const CommentSortOrder = { Recent: 0, Popular: 1 } as const
