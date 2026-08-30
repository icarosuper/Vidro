import {
	useInfiniteQuery,
	useMutation,
	useQuery,
	useQueryClient,
} from "@tanstack/react-query";
import { toastApiError } from "#/shared/lib/toast-error";
import {
	addVideoToPlaylist,
	createPlaylist,
	deletePlaylist,
	getPlaylist,
	listChannelPlaylists,
	listUserPlaylists,
	removeVideoFromPlaylist,
	updatePlaylist,
} from "./api";
import type { CreatePlaylistRequest, UpdatePlaylistRequest } from "./types";

export const playlistKeys = {
	detail: (playlistId: string) => ["playlists", playlistId] as const,
	channelPlaylists: (username: string, handle: string) =>
		["playlists", "channel", username, handle] as const,
	userPlaylists: (username: string) => ["playlists", "user", username] as const,
};

const PLAYLISTS_LIMIT = 20;

export function usePlaylist(playlistId: string) {
	return useQuery({
		queryKey: playlistKeys.detail(playlistId),
		queryFn: ({ signal }) => getPlaylist(playlistId, signal),
	});
}

export function useChannelPlaylists(
	username: string | undefined,
	handle: string | undefined,
) {
	return useInfiniteQuery({
		queryKey: playlistKeys.channelPlaylists(username ?? "", handle ?? ""),
		queryFn: ({ signal, pageParam }) =>
			listChannelPlaylists(
				username!,
				handle!,
				PLAYLISTS_LIMIT,
				pageParam as string | undefined,
				signal,
			),
		initialPageParam: undefined as string | undefined,
		getNextPageParam: (lastPage) => lastPage.nextCursor ?? undefined,
		enabled: !!username && !!handle,
	});
}

export function useUserPlaylists(username: string | undefined) {
	return useInfiniteQuery({
		queryKey: playlistKeys.userPlaylists(username ?? ""),
		queryFn: ({ signal, pageParam }) =>
			listUserPlaylists(
				username!,
				PLAYLISTS_LIMIT,
				pageParam as string | undefined,
				signal,
			),
		initialPageParam: undefined as string | undefined,
		getNextPageParam: (lastPage) => lastPage.nextCursor ?? undefined,
		enabled: !!username,
	});
}

export function useCreatePlaylist() {
	return useMutation({
		mutationFn: (data: CreatePlaylistRequest) => createPlaylist(data),
		onError: toastApiError,
	});
}

export function useUpdatePlaylist(playlistId: string) {
	const queryClient = useQueryClient();

	return useMutation({
		mutationFn: (data: UpdatePlaylistRequest) =>
			updatePlaylist(playlistId, data),
		onError: toastApiError,
		onSuccess: () => {
			queryClient.invalidateQueries({
				queryKey: playlistKeys.detail(playlistId),
			});
		},
	});
}

export function useDeletePlaylist() {
	const queryClient = useQueryClient();

	return useMutation({
		mutationFn: (playlistId: string) => deletePlaylist(playlistId),
		onError: toastApiError,
		onSuccess: () => {
			queryClient.invalidateQueries({ queryKey: ["playlists"] });
		},
	});
}

export function useAddVideoToPlaylist(playlistId: string) {
	const queryClient = useQueryClient();

	return useMutation({
		mutationFn: (videoId: string) => addVideoToPlaylist(playlistId, videoId),
		onError: toastApiError,
		onSuccess: () => {
			queryClient.invalidateQueries({
				queryKey: playlistKeys.detail(playlistId),
			});
		},
	});
}

export function useRemoveVideoFromPlaylist(playlistId: string) {
	const queryClient = useQueryClient();

	return useMutation({
		mutationFn: (videoId: string) =>
			removeVideoFromPlaylist(playlistId, videoId),
		onError: toastApiError,
		onSuccess: () => {
			queryClient.invalidateQueries({
				queryKey: playlistKeys.detail(playlistId),
			});
		},
	});
}
