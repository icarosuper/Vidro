import { apiClient } from "#/shared/lib/api-client";
import type {
	CreatePlaylistRequest,
	CreatePlaylistResponse,
	Playlist,
	PlaylistsPage,
	UpdatePlaylistRequest,
} from "./types";

export function createPlaylist(
	data: CreatePlaylistRequest,
	signal?: AbortSignal,
) {
	return apiClient.post<CreatePlaylistResponse>("/v1/playlists", data, signal);
}

export function getPlaylist(playlistId: string, signal?: AbortSignal) {
	return apiClient.get<Playlist>(`/v1/playlists/${playlistId}`, signal);
}

export function updatePlaylist(
	playlistId: string,
	data: UpdatePlaylistRequest,
	signal?: AbortSignal,
) {
	return apiClient.put<void>(`/v1/playlists/${playlistId}`, data, signal);
}

export function deletePlaylist(playlistId: string, signal?: AbortSignal) {
	return apiClient.delete<void>(`/v1/playlists/${playlistId}`, signal);
}

export function listChannelPlaylists(
	username: string,
	handle: string,
	limit: number,
	cursor: string | undefined,
	signal?: AbortSignal,
) {
	const params = new URLSearchParams({ limit: String(limit) });
	if (cursor) params.set("cursor", cursor);
	return apiClient.get<PlaylistsPage>(
		`/v1/users/${username}/channels/${handle}/playlists?${params.toString()}`,
		signal,
	);
}

export function listUserPlaylists(
	username: string,
	limit: number,
	cursor: string | undefined,
	signal?: AbortSignal,
) {
	const params = new URLSearchParams({ limit: String(limit) });
	if (cursor) params.set("cursor", cursor);
	return apiClient.get<PlaylistsPage>(
		`/v1/users/${username}/playlists?${params.toString()}`,
		signal,
	);
}

export function addVideoToPlaylist(
	playlistId: string,
	videoId: string,
	signal?: AbortSignal,
) {
	return apiClient.post<void>(
		`/v1/playlists/${playlistId}/items`,
		{ videoId },
		signal,
	);
}

export function removeVideoFromPlaylist(
	playlistId: string,
	videoId: string,
	signal?: AbortSignal,
) {
	return apiClient.delete<void>(
		`/v1/playlists/${playlistId}/items/${videoId}`,
		signal,
	);
}
