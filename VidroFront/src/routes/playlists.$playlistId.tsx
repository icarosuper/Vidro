import { createFileRoute } from "@tanstack/react-router";
import { Pencil, Trash2 } from "lucide-react";
import { useState } from "react";
import { Button } from "#/components/ui/button";
import {
	Dialog,
	DialogContent,
	DialogHeader,
	DialogTitle,
} from "#/components/ui/dialog";
import { Separator } from "#/components/ui/separator";
import { Skeleton } from "#/components/ui/skeleton";
import { useCurrentUser } from "#/features/users/hooks";
import { EditPlaylistForm } from "#/features/playlists/components/EditPlaylistForm";
import { PlaylistItemList } from "#/features/playlists/components/PlaylistItemList";
import { getPlaylist } from "#/features/playlists/api";
import {
	playlistKeys,
	useDeletePlaylist,
	usePlaylist,
} from "#/features/playlists/hooks";

export const Route = createFileRoute("/playlists/$playlistId")({
	loader: async ({ params, context: { queryClient } }) => {
		await queryClient.prefetchQuery({
			queryKey: playlistKeys.detail(params.playlistId),
			queryFn: () => getPlaylist(params.playlistId),
		});
	},
	component: PlaylistPage,
});

function PlaylistPageSkeleton() {
	return (
		<div className="mx-auto max-w-2xl px-4 py-8 space-y-4">
			<Skeleton className="h-8 w-1/2" />
			<Skeleton className="h-4 w-2/3" />
			<Skeleton className="h-4 w-1/4" />
			<Separator />
			<div className="space-y-3">
				{Array.from({ length: 5 }).map((_, i) => (
					// biome-ignore lint/suspicious/noArrayIndexKey: skeleton list
					<Skeleton key={i} className="h-16 w-full rounded-lg" />
				))}
			</div>
		</div>
	);
}

function PlaylistPage() {
	const { playlistId } = Route.useParams();
	const navigate = Route.useNavigate();
	const { data: playlist, isPending, isError } = usePlaylist(playlistId);
	const { data: currentUser } = useCurrentUser();
	const deletePlaylist = useDeletePlaylist();
	const [editOpen, setEditOpen] = useState(false);

	if (isPending) return <PlaylistPageSkeleton />;
	if (isError || !playlist) {
		return (
			<div className="mx-auto max-w-2xl px-4 py-8">
				<p className="text-muted-foreground">Playlist not found.</p>
			</div>
		);
	}

	const isOwner =
		!!currentUser && playlist.channelId
			? false // channel playlists: ownership checked via channel, not shown here
			: !!currentUser;

	function handleDelete() {
		deletePlaylist.mutate(playlistId, {
			onSuccess: () => navigate({ to: "/" }),
		});
	}

	return (
		<div className="mx-auto max-w-2xl px-4 py-8 space-y-6">
			<div className="space-y-2">
				<div className="flex items-start justify-between gap-4">
					<h1 className="text-2xl font-bold">{playlist.name}</h1>
					{isOwner && (
						<div className="flex items-center gap-2 shrink-0">
							<Button
								variant="ghost"
								size="icon"
								onClick={() => setEditOpen(true)}
								aria-label="Edit playlist"
							>
								<Pencil className="h-4 w-4" />
							</Button>
							<Button
								variant="ghost"
								size="icon"
								className="text-muted-foreground hover:text-destructive"
								disabled={deletePlaylist.isPending}
								onClick={handleDelete}
								aria-label="Delete playlist"
							>
								<Trash2 className="h-4 w-4" />
							</Button>
						</div>
					)}
				</div>

				{playlist.description && (
					<p className="text-sm text-muted-foreground">
						{playlist.description}
					</p>
				)}

				<p className="text-xs text-muted-foreground">
					{playlist.videoCount} {playlist.videoCount === 1 ? "video" : "videos"}{" "}
					· {playlist.visibility.value}
				</p>
			</div>

			<Separator />

			<PlaylistItemList
				playlistId={playlistId}
				items={playlist.items}
				isOwner={isOwner}
			/>

			{isOwner && (
				<Dialog open={editOpen} onOpenChange={setEditOpen}>
					<DialogContent>
						<DialogHeader>
							<DialogTitle>Edit playlist</DialogTitle>
						</DialogHeader>
						<EditPlaylistForm
							playlist={playlist}
							onSuccess={() => setEditOpen(false)}
						/>
					</DialogContent>
				</Dialog>
			)}
		</div>
	);
}
