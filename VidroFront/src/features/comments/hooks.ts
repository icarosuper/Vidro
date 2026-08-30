import { useInfiniteQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import type { InfiniteData } from '@tanstack/react-query'
import { ReactionType } from '#/shared/types'
import {
  addComment,
  deleteComment,
  editComment,
  listComments,
  listReplies,
  reactToComment,
  removeCommentReaction,
} from './api'
import type { CommentsPage, RepliesPage } from './types'

export const commentKeys = {
  list: (videoId: string, sort: 0 | 1) => ['comments', videoId, sort] as const,
  replies: (commentId: string) => ['comments', 'replies', commentId] as const,
}

const COMMENTS_LIMIT = 20
const REPLIES_LIMIT = 10

type ReactionFields = { userReaction: { id: number; value: string } | null; likeCount: number; dislikeCount: number }

function withReaction<T extends ReactionFields>(item: T, type: number): T {
  const prev = item.userReaction
  return {
    ...item,
    userReaction: { id: type, value: type === ReactionType.Like ? 'Like' : 'Dislike' },
    likeCount: item.likeCount + (type === ReactionType.Like ? 1 : 0) - (prev?.id === ReactionType.Like ? 1 : 0),
    dislikeCount: item.dislikeCount + (type === ReactionType.Dislike ? 1 : 0) - (prev?.id === ReactionType.Dislike ? 1 : 0),
  }
}

function withoutReaction<T extends ReactionFields>(item: T): T {
  const prev = item.userReaction
  return {
    ...item,
    userReaction: null,
    likeCount: item.likeCount - (prev?.id === ReactionType.Like ? 1 : 0),
    dislikeCount: item.dislikeCount - (prev?.id === ReactionType.Dislike ? 1 : 0),
  }
}

export function useComments(videoId: string, sort: 0 | 1) {
  return useInfiniteQuery({
    queryKey: commentKeys.list(videoId, sort),
    queryFn: ({ signal, pageParam }) =>
      listComments(videoId, sort, COMMENTS_LIMIT, pageParam as string | undefined, signal),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage) => lastPage.nextCursor ?? undefined,
    // Popular sort returns a flat list with no cursor — disable pagination
    enabled: true,
  })
}

export function useReplies(commentId: string, enabled: boolean) {
  return useInfiniteQuery({
    queryKey: commentKeys.replies(commentId),
    queryFn: ({ signal, pageParam }) =>
      listReplies(commentId, REPLIES_LIMIT, pageParam as string | undefined, signal),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage) => lastPage.nextCursor ?? undefined,
    enabled,
  })
}

export function useAddComment(videoId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({
      content,
      parentCommentId,
    }: {
      content: string
      parentCommentId?: string
    }) => addComment(videoId, content, parentCommentId),
    onSuccess: (_data, variables) => {
      if (variables.parentCommentId) {
        queryClient.invalidateQueries({
          queryKey: commentKeys.replies(variables.parentCommentId),
        })
      } else {
        // Invalidate recent sort — new comments appear at the top
        queryClient.invalidateQueries({ queryKey: commentKeys.list(videoId, 0) })
      }
    },
  })
}

export function useEditComment(videoId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ commentId, content }: { commentId: string; content: string }) =>
      editComment(commentId, content),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['comments', videoId] })
    },
  })
}

export function useDeleteComment(videoId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (commentId: string) => deleteComment(commentId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['comments', videoId] })
    },
  })
}

export function useReactToComment(videoId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ commentId, type }: { commentId: string; type: number; parentCommentId?: string }) =>
      reactToComment(commentId, type),
    onMutate: async ({ commentId, type, parentCommentId }) => {
      if (parentCommentId) {
        const key = commentKeys.replies(parentCommentId)
        await queryClient.cancelQueries({ queryKey: key })
        const snapshot = queryClient.getQueryData<InfiniteData<RepliesPage>>(key)
        queryClient.setQueryData<InfiniteData<RepliesPage>>(key, (old) => {
          if (!old) return old
          return {
            ...old,
            pages: old.pages.map((page) => ({
              ...page,
              replies: page.replies.map((r) => (r.commentId === commentId ? withReaction(r, type) : r)),
            })),
          }
        })
        return { isReply: true as const, key, snapshot }
      }

      await queryClient.cancelQueries({ queryKey: ['comments', videoId] })
      const snapshot0 = queryClient.getQueryData<InfiniteData<CommentsPage>>(commentKeys.list(videoId, 0))
      const snapshot1 = queryClient.getQueryData<InfiniteData<CommentsPage>>(commentKeys.list(videoId, 1))
      const update = (old: InfiniteData<CommentsPage> | undefined) => {
        if (!old) return old
        return {
          ...old,
          pages: old.pages.map((page) => ({
            ...page,
            comments: page.comments.map((c) => (c.commentId === commentId ? withReaction(c, type) : c)),
          })),
        }
      }
      queryClient.setQueryData<InfiniteData<CommentsPage>>(commentKeys.list(videoId, 0), update)
      queryClient.setQueryData<InfiniteData<CommentsPage>>(commentKeys.list(videoId, 1), update)
      return { isReply: false as const, snapshot0, snapshot1 }
    },
    onError: (_err, { parentCommentId }, context) => {
      if (!context) return
      if (context.isReply) {
        queryClient.setQueryData(context.key, context.snapshot)
      } else {
        queryClient.setQueryData(commentKeys.list(videoId, 0), context.snapshot0)
        queryClient.setQueryData(commentKeys.list(videoId, 1), context.snapshot1)
      }
    },
    onSettled: (_data, _err, { parentCommentId }) => {
      if (parentCommentId) {
        queryClient.invalidateQueries({ queryKey: commentKeys.replies(parentCommentId) })
      } else {
        queryClient.invalidateQueries({ queryKey: ['comments', videoId] })
      }
    },
  })
}

export function useRemoveCommentReaction(videoId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ commentId }: { commentId: string; parentCommentId?: string }) =>
      removeCommentReaction(commentId),
    onMutate: async ({ commentId, parentCommentId }) => {
      if (parentCommentId) {
        const key = commentKeys.replies(parentCommentId)
        await queryClient.cancelQueries({ queryKey: key })
        const snapshot = queryClient.getQueryData<InfiniteData<RepliesPage>>(key)
        queryClient.setQueryData<InfiniteData<RepliesPage>>(key, (old) => {
          if (!old) return old
          return {
            ...old,
            pages: old.pages.map((page) => ({
              ...page,
              replies: page.replies.map((r) => (r.commentId === commentId ? withoutReaction(r) : r)),
            })),
          }
        })
        return { isReply: true as const, key, snapshot }
      }

      await queryClient.cancelQueries({ queryKey: ['comments', videoId] })
      const snapshot0 = queryClient.getQueryData<InfiniteData<CommentsPage>>(commentKeys.list(videoId, 0))
      const snapshot1 = queryClient.getQueryData<InfiniteData<CommentsPage>>(commentKeys.list(videoId, 1))
      const update = (old: InfiniteData<CommentsPage> | undefined) => {
        if (!old) return old
        return {
          ...old,
          pages: old.pages.map((page) => ({
            ...page,
            comments: page.comments.map((c) => (c.commentId === commentId ? withoutReaction(c) : c)),
          })),
        }
      }
      queryClient.setQueryData<InfiniteData<CommentsPage>>(commentKeys.list(videoId, 0), update)
      queryClient.setQueryData<InfiniteData<CommentsPage>>(commentKeys.list(videoId, 1), update)
      return { isReply: false as const, snapshot0, snapshot1 }
    },
    onError: (_err, { parentCommentId }, context) => {
      if (!context) return
      if (context.isReply) {
        queryClient.setQueryData(context.key, context.snapshot)
      } else {
        queryClient.setQueryData(commentKeys.list(videoId, 0), context.snapshot0)
        queryClient.setQueryData(commentKeys.list(videoId, 1), context.snapshot1)
      }
    },
    onSettled: (_data, _err, { parentCommentId }) => {
      if (parentCommentId) {
        queryClient.invalidateQueries({ queryKey: commentKeys.replies(parentCommentId) })
      } else {
        queryClient.invalidateQueries({ queryKey: ['comments', videoId] })
      }
    },
  })
}
