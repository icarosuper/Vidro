import type { EnumValue } from '#/shared/types'

export type CreateVideoRequest = {
  title: string
  description: string | null
  tags: string[]
  visibility: number
}

export type CreateVideoResponse = {
  videoId: string
  uploadUrl: string
  uploadExpiresAt: string
}

export type UpdateVideoRequest = {
  title: string
  description: string | null
  tags: string[]
  visibility: number
}

export type UpdateVideoResponse = {
  videoId: string
  title: string
  description: string | null
  tags: string[]
  visibility: EnumValue
}

export type ThumbnailUploadResponse = {
  uploadUrl: string
  uploadExpiresAt: string
}

export type VideoSummary = {
  videoId: string
  channelId: string
  channelHandle: string
  channelName: string
  ownerUsername: string
  channelAvatarUrl: string | null
  title: string
  description: string | null
  tags: string[]
  viewCount: number
  likeCount: number
  thumbnailUrls: string[]
  createdAt: string
}

export type Video = {
  videoId: string
  channelId: string
  channelHandle: string
  channelName: string
  ownerUsername: string
  channelAvatarUrl: string | null
  title: string
  description: string | null
  tags: string[]
  visibility: EnumValue
  status: EnumValue
  viewCount: number
  likeCount: number
  dislikeCount: number
  commentCount: number
  thumbnailUrls: string[]
  videoUrl: string | null
  isFollowingChannel: boolean
  userReaction: EnumValue | null
  createdAt: string
}

export type TrendingResponse = {
  videos: VideoSummary[]
}

export type FeedPage = {
  videos: VideoSummary[]
  nextCursor: string | null
}

export type ChannelVideoSummary = {
  videoId: string
  channelHandle: string
  ownerUsername: string
  title: string
  description: string | null
  tags: string[]
  visibility: EnumValue
  status: EnumValue
  viewCount: number
  likeCount: number
  thumbnailUrls: string[]
  channelAvatarUrl: string | null
  createdAt: string
}

export type ChannelVideosPage = {
  videos: ChannelVideoSummary[]
  nextCursor: string | null
}
