import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ReactionType, VideoStatus } from '#/shared/types'
import { toastApiError } from '#/shared/lib/toast-error'
import {
  createVideo,
  getFeed,
  getChannelVideos,
  getTrending,
  getVideo,
  reactToVideo,
  registerView,
  removeReaction,
  updateVideo,
  uploadThumbnail,
} from './api'
import type { CreateVideoRequest, UpdateVideoRequest, Video } from './types'

export const videoKeys = {
  trending: () => ['videos', 'trending'] as const,
  feed: () => ['videos', 'feed'] as const,
  detail: (videoId: string) => ['videos', videoId] as const,
  channelVideos: (username: string, handle: string) => ['videos', 'channel', username, handle] as const,
}

const TRENDING_LIMIT = 20
const FEED_LIMIT = 20

export function useTrending() {
  return useQuery({
    queryKey: videoKeys.trending(),
    queryFn: ({ signal }) => getTrending(TRENDING_LIMIT, signal),
  })
}

export function useFeed(enabled: boolean) {
  return useInfiniteQuery({
    queryKey: videoKeys.feed(),
    queryFn: ({ signal, pageParam }) =>
      getFeed(FEED_LIMIT, pageParam as string | undefined, signal),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage) =>
      lastPage.nextCursor
        ? lastPage.nextCursor
        : undefined,
    enabled,
  })
}

export function useVideo(videoId: string) {
  return useQuery({
    queryKey: videoKeys.detail(videoId),
    queryFn: ({ signal }) => getVideo(videoId, signal),
  })
}

export function useRegisterView(videoId: string) {
  return useMutation({
    mutationFn: () => registerView(videoId),
  })
}

export function useReactToVideo(videoId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (type: ReactionType) => reactToVideo(videoId, type),
    onError: toastApiError,
    onSuccess: (_data, reactionType) => {
      queryClient.setQueryData(videoKeys.detail(videoId), (prev: Video | undefined) => {
        if (!prev) return prev
        const isNewReactionLike = reactionType === ReactionType.Like
        const previousReactionId = prev.userReaction?.id ?? null
        const hadOppositeReaction =
          previousReactionId !== null && previousReactionId !== reactionType
        return {
          ...prev,
          likeCount: isNewReactionLike
            ? prev.likeCount + 1
            : hadOppositeReaction
              ? prev.likeCount - 1
              : prev.likeCount,
          dislikeCount: !isNewReactionLike
            ? prev.dislikeCount + 1
            : hadOppositeReaction
              ? prev.dislikeCount - 1
              : prev.dislikeCount,
          userReaction: {
            id: reactionType,
            value: isNewReactionLike ? 'Like' : 'Dislike',
          },
        }
      })
    },
  })
}

export function useRemoveReaction(videoId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: () => removeReaction(videoId),
    onError: toastApiError,
    onSuccess: () => {
      queryClient.setQueryData(videoKeys.detail(videoId), (prev: Video | undefined) => {
        if (!prev) return prev
        const wasLike = prev.userReaction?.id === ReactionType.Like
        return {
          ...prev,
          likeCount: wasLike ? prev.likeCount - 1 : prev.likeCount,
          dislikeCount: !wasLike ? prev.dislikeCount - 1 : prev.dislikeCount,
          userReaction: null,
        }
      })
    },
  })
}

const VIDEO_PROCESSING_POLL_INTERVAL_MS = 3000

export function useVideoStatus(videoId: string | null) {
  return useQuery({
    queryKey: videoKeys.detail(videoId ?? ''),
    queryFn: ({ signal }) => getVideo(videoId!, signal),
    enabled: !!videoId,
    refetchInterval: (query) => {
      const status = query.state.data?.status.id
      const isTerminal =
        status === VideoStatus.Ready || status === VideoStatus.Failed
      return isTerminal
        ? false
        : VIDEO_PROCESSING_POLL_INTERVAL_MS
    },
  })
}

export function useCreateVideo(username: string, handle: string) {
  return useMutation({
    mutationFn: (data: CreateVideoRequest) => createVideo(username, handle, data),
  })
}

export function useUpdateVideo(videoId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (data: UpdateVideoRequest) => updateVideo(videoId, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: videoKeys.detail(videoId) })
      queryClient.invalidateQueries({ queryKey: ['videos', 'channel'] })
    },
  })
}

const CHANNEL_VIDEOS_LIMIT = 20

export function useChannelVideos(username: string | undefined, handle: string | undefined) {
  return useInfiniteQuery({
    queryKey: videoKeys.channelVideos(username ?? '', handle ?? ''),
    queryFn: ({ signal, pageParam }) =>
      getChannelVideos(username!, handle!, CHANNEL_VIDEOS_LIMIT, pageParam as string | undefined, signal),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage) =>
      lastPage.nextCursor
        ? lastPage.nextCursor
        : undefined,
    enabled: !!username && !!handle,
  })
}

export function useUploadThumbnail(videoId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (file: File) => uploadThumbnail(videoId, file),
    onError: toastApiError,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: videoKeys.detail(videoId) })
      queryClient.invalidateQueries({ queryKey: ['videos', 'channel'] })
    },
  })
}
