import type { EnumValue } from '#/shared/types'

export type CommentSummary = {
  commentId: string
  userId: string
  username: string
  content: string | null
  isDeleted: boolean
  likeCount: number
  dislikeCount: number
  replyCount: number
  userReaction: EnumValue | null
  createdAt: string
  updatedAt: string | null
}

export type CommentsPage = {
  comments: CommentSummary[]
  nextCursor: string | null
}

export type ReplySummary = {
  commentId: string
  userId: string
  username: string
  content: string | null
  isDeleted: boolean
  likeCount: number
  dislikeCount: number
  userReaction: EnumValue | null
  createdAt: string
  updatedAt: string | null
}

export type RepliesPage = {
  replies: ReplySummary[]
  nextCursor: string | null
}
