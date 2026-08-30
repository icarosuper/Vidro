import type { EnumValue } from "#/shared/types";

export type PlaylistSummary = {
	id: string;
	name: string;
	description: string | null;
	visibility: EnumValue;
	videoCount: number;
	createdAt: string;
};

export type PlaylistItem = {
	videoId: string;
	title: string;
	thumbnailUrl: string | null;
	durationSeconds: number | null;
};

export type Playlist = {
	playlistId: string;
	name: string;
	description: string | null;
	visibility: EnumValue;
	scope: EnumValue;
	videoCount: number;
	channelId: string | null;
	createdAt: string;
	items: PlaylistItem[];
};

export type PlaylistsPage = {
	items: PlaylistSummary[];
	nextCursor: string | null;
};

export type CreatePlaylistRequest = {
	name: string;
	description: string | null;
	visibility: number;
	scope: number;
	channelId: string | null;
};

export type CreatePlaylistResponse = {
	playlistId: string;
};

export type UpdatePlaylistRequest = {
	name: string;
	description: string | null;
	visibility: number;
};
