// The API's request/response shapes under the names the UI uses. The shapes themselves come from
// `schema.d.ts`, generated from `VidroApi/openapi/v1.json` by `bun run openapi` — never edit them
// here. Responses are the `data` inside the `{ data: T }` envelope, which `apiClient` unwraps.
import type { components } from './schema'

type Schemas = components['schemas']

export type EnumValue = Schemas['EnumValue']

// The body of a domain or request error (any 4xx with a body, except the FluentValidation 400).
export type ApiError = Schemas['ApiErrorResponse']

export type SignUpRequest = Schemas['SignUpRequest']
export type SignInRequest = Schemas['SignInRequest']
export type AuthTokens = Schemas['SignInResponse']

export type UserProfile = Schemas['GetCurrentUserResponse']
export type UploadAvatarResponse = Schemas['UploadAvatarResponse']

export type CreateChannelRequest = Schemas['CreateChannelRequest']
export type CreateChannelResponse = Schemas['CreateChannelResponse']
export type UpdateChannelRequest = Schemas['UpdateChannelRequest']
export type Channel = Schemas['GetChannelResponse']
export type ChannelSummary = Schemas['ListUserChannelsResponseChannelSummary']
export type ChannelsResponse = Schemas['ListUserChannelsResponse']

export type CreateVideoRequest = Schemas['CreateVideoRequest']
export type CreateVideoResponse = Schemas['CreateVideoResponse']
export type UpdateVideoRequest = Schemas['UpdateVideoRequest']
export type UpdateVideoResponse = Schemas['UpdateVideoResponse']
export type ThumbnailUploadResponse = Schemas['UploadVideoThumbnailResponse']
export type Video = Schemas['GetVideoResponse']
export type VideoSummary = Schemas['ListTrendingVideosResponseVideoSummary']
export type TrendingResponse = Schemas['ListTrendingVideosResponse']
export type FeedPage = Schemas['ListFeedVideosResponse']
export type SearchPage = Schemas['SearchVideosResponse']
export type ChannelVideoSummary =
  Schemas['ListChannelVideosResponseVideoSummary']
export type ChannelVideosPage = Schemas['ListChannelVideosResponse']

export type CommentSummary = Schemas['ListCommentsResponseCommentSummary']
export type CommentsPage = Schemas['ListCommentsResponse']
export type ReplySummary = Schemas['ListRepliesResponseReplySummary']
export type RepliesPage = Schemas['ListRepliesResponse']

export type Playlist = Schemas['GetPlaylistResponse']
export type PlaylistItem = Schemas['GetPlaylistResponsePlaylistItemResponse']
export type PlaylistSummary = Schemas['ListPlaylistsByUserPlaylistResponse']
export type PlaylistsPage =
  Schemas['ListPlaylistsByUserPlaylistResponsePagedResult']
export type CreatePlaylistRequest = Schemas['CreatePlaylistRequest']
export type CreatePlaylistResponse = Schemas['CreatePlaylistResponse']
export type UpdatePlaylistRequest = Schemas['UpdatePlaylistRequest']
