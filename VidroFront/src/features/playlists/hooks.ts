import {
  skipToken,
  useInfiniteQuery,
  useMutation,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query'
import { toastApiError } from '#/shared/lib/toast-error'
import {
  addVideoToPlaylist,
  createPlaylist,
  deletePlaylist,
  getPlaylist,
  listChannelPlaylists,
  listUserPlaylists,
  removeVideoFromPlaylist,
  updatePlaylist,
} from './api'
import type {
  CreatePlaylistRequest,
  PlaylistSummary,
  UpdatePlaylistRequest,
} from './types'

export const playlistKeys = {
  detail: (playlistId: string) => ['playlists', playlistId] as const,
  channelPlaylists: (username: string, handle: string) =>
    ['playlists', 'channel', username, handle] as const,
  userPlaylists: (username: string) => ['playlists', 'user', username] as const,
  // Under 'playlists', so creating or deleting a playlist refreshes the Save picker too.
  saveTargets: (videoId: string) => ['playlists', 'save', videoId] as const,
}

const PLAYLISTS_LIMIT = 20

export function usePlaylist(playlistId: string) {
  return useQuery({
    queryKey: playlistKeys.detail(playlistId),
    queryFn: ({ signal }) => getPlaylist(playlistId, signal),
  })
}

export function useChannelPlaylists(
  username: string | undefined,
  handle: string | undefined,
) {
  return useInfiniteQuery({
    queryKey: playlistKeys.channelPlaylists(username ?? '', handle ?? ''),
    queryFn:
      username && handle
        ? ({ signal, pageParam }) =>
            listChannelPlaylists(
              username,
              handle,
              PLAYLISTS_LIMIT,
              pageParam as string | undefined,
              signal,
            )
        : skipToken,
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage) => lastPage.nextCursor ?? undefined,
  })
}

export function useUserPlaylists(username: string | undefined) {
  return useInfiniteQuery({
    queryKey: playlistKeys.userPlaylists(username ?? ''),
    queryFn: username
      ? ({ signal, pageParam }) =>
          listUserPlaylists(
            username,
            PLAYLISTS_LIMIT,
            pageParam as string | undefined,
            signal,
          )
      : skipToken,
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (lastPage) => lastPage.nextCursor ?? undefined,
  })
}

export function useCreatePlaylist() {
  return useMutation({
    mutationFn: (data: CreatePlaylistRequest) => createPlaylist(data),
    onError: toastApiError,
  })
}

export function useUpdatePlaylist(playlistId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (data: UpdatePlaylistRequest) =>
      updatePlaylist(playlistId, data),
    onError: toastApiError,
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: playlistKeys.detail(playlistId),
      })
    },
  })
}

export function useDeletePlaylist() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (playlistId: string) => deletePlaylist(playlistId),
    onError: toastApiError,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['playlists'] })
    },
  })
}

export function useRemoveVideoFromPlaylist(playlistId: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (videoId: string) =>
      removeVideoFromPlaylist(playlistId, videoId),
    onError: toastApiError,
    onSuccess: () => {
      queryClient.invalidateQueries({
        queryKey: playlistKeys.detail(playlistId),
      })
    },
  })
}

// The picker shows one page at the API's maximum and no "load more": a hundred playlists is
// already more than a menu can hold.
const SAVE_TARGETS_LIMIT = 100

export type SaveTarget = PlaylistSummary & { group: 'personal' | 'channel' }

type SaveTargetsInput = {
  videoId: string
  // The signed-in user; undefined while it loads or when signed out (no request then).
  username: string | undefined
  // The video's channel, only when it is the user's own: a channel playlist takes only that
  // channel's videos, so another channel's playlists would always refuse.
  ownChannelHandle: string | undefined
}

async function fetchSaveTargets(
  {
    videoId,
    username,
    ownChannelHandle,
  }: SaveTargetsInput & { username: string },
  signal: AbortSignal,
): Promise<SaveTarget[]> {
  const [personal, channel] = await Promise.all([
    listUserPlaylists(username, SAVE_TARGETS_LIMIT, undefined, signal, videoId),
    ownChannelHandle
      ? listChannelPlaylists(
          username,
          ownChannelHandle,
          SAVE_TARGETS_LIMIT,
          undefined,
          signal,
          videoId,
        )
      : null,
  ])
  const personalTargets = personal.items.map(
    (playlist): SaveTarget => ({ ...playlist, group: 'personal' }),
  )
  const channelTargets = (channel?.items ?? []).map(
    (playlist): SaveTarget => ({ ...playlist, group: 'channel' }),
  )
  return [...personalTargets, ...channelTargets]
}

// The playlists a video can be saved to, each with `containsVideo`.
export function useSaveTargets(input: SaveTargetsInput) {
  const { videoId, username } = input
  return useQuery({
    queryKey: [
      ...playlistKeys.saveTargets(videoId),
      input.ownChannelHandle ?? null,
    ],
    queryFn: username
      ? ({ signal }) => fetchSaveTargets({ ...input, username }, signal)
      : skipToken,
  })
}

type ToggleInput = { playlistId: string; save: boolean }

// Checks or unchecks a playlist in the picker. The box flips at once (optimistic) and flips back
// if the API refuses; afterwards every playlist query refetches, so counts and pages follow.
export function useToggleVideoInPlaylist(videoId: string) {
  const queryClient = useQueryClient()
  const targetsKey = playlistKeys.saveTargets(videoId)

  return useMutation({
    mutationFn: ({ playlistId, save }: ToggleInput) =>
      save
        ? addVideoToPlaylist(playlistId, videoId)
        : removeVideoFromPlaylist(playlistId, videoId),
    onMutate: async ({ playlistId, save }) => {
      await queryClient.cancelQueries({ queryKey: targetsKey })
      queryClient.setQueriesData<SaveTarget[]>(
        { queryKey: targetsKey },
        (targets) =>
          targets?.map((target) =>
            target.id === playlistId
              ? { ...target, containsVideo: save }
              : target,
          ),
      )
    },
    onError: toastApiError,
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: ['playlists'] })
    },
  })
}
