import { apiClient } from '#/shared/lib/api-client'
import { ReactionType } from '#/shared/types'
import type { CommentsPage, RepliesPage } from './types'

export function listComments(
  videoId: string,
  sort: 0 | 1,
  limit: number,
  cursor: string | undefined,
  signal?: AbortSignal,
) {
  const params = new URLSearchParams({ sort: String(sort), limit: String(limit) })
  if (cursor) params.set('cursor', cursor)
  return apiClient.get<CommentsPage>(`/v1/videos/${videoId}/comments?${params.toString()}`, signal)
}

export function listReplies(
  commentId: string,
  limit: number,
  cursor: string | undefined,
  signal?: AbortSignal,
) {
  const params = new URLSearchParams({ limit: String(limit) })
  if (cursor) params.set('cursor', cursor)
  return apiClient.get<RepliesPage>(`/v1/comments/${commentId}/replies?${params.toString()}`, signal)
}

export function addComment(
  videoId: string,
  content: string,
  parentCommentId?: string,
  signal?: AbortSignal,
) {
  return apiClient.post<{ commentId: string }>(
    `/v1/videos/${videoId}/comments`,
    { content, parentCommentId },
    signal,
  )
}

export function editComment(commentId: string, content: string, signal?: AbortSignal) {
  return apiClient.put<void>(`/v1/comments/${commentId}`, { content }, signal)
}

export function deleteComment(commentId: string, signal?: AbortSignal) {
  return apiClient.delete<void>(`/v1/comments/${commentId}`, signal)
}

export function reactToComment(commentId: string, type: ReactionType, signal?: AbortSignal) {
  return apiClient.post<void>(`/v1/comments/${commentId}/reactions`, { type }, signal)
}

export function removeCommentReaction(commentId: string, signal?: AbortSignal) {
  return apiClient.delete<void>(`/v1/comments/${commentId}/reactions`, signal)
}
