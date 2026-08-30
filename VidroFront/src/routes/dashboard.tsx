import { createFileRoute, redirect } from "@tanstack/react-router";
import { Plus, Trash2 } from "lucide-react";
import { useState } from "react";
import { Button } from "#/components/ui/button";
import {
	Dialog,
	DialogContent,
	DialogHeader,
	DialogTitle,
} from "#/components/ui/dialog";
import { Skeleton } from "#/components/ui/skeleton";
import { CreatePlaylistForm } from "#/features/playlists/components/CreatePlaylistForm";
import { PlaylistCard } from "#/features/playlists/components/PlaylistCard";
import {
	useDeletePlaylist,
	useUserPlaylists,
} from "#/features/playlists/hooks";
import { useCurrentUser } from "#/features/users/hooks";
import { tokenStore } from "#/shared/lib/token-store";

export const Route = createFileRoute("/dashboard")({
	beforeLoad: () => {
		const isServer = typeof window === "undefined";
		if (isServer) return;

		const isAuthenticated = tokenStore.get() !== null;
		if (!isAuthenticated) {
			throw redirect({ to: "/" });
		}
	},
	component: DashboardPage,
});

function DashboardPage() {
	const { data: currentUser } = useCurrentUser();
	const [newPlaylistOpen, setNewPlaylistOpen] = useState(false);

	const {
		data: playlistsData,
		isPending: playlistsPending,
		fetchNextPage,
		hasNextPage,
		isFetchingNextPage,
		refetch,
	} = useUserPlaylists(currentUser?.username);

	const deletePlaylist = useDeletePlaylist();

	const playlists = playlistsData?.pages.flatMap((page) => page.items) ?? [];

	return (
		<main className="page-container py-8 space-y-8">
			<h1 className="text-2xl font-bold">Dashboard</h1>

			{/* Personal playlists */}
			<section className="space-y-4">
				<div className="flex items-center justify-between">
					<h2 className="text-xs font-semibold text-muted-foreground uppercase tracking-wide">
						My playlists
						{!playlistsPending &&
							playlists.length > 0 &&
							` (${playlists.length}${hasNextPage ? "+" : ""})`}
					</h2>
					<Button
						variant="ghost"
						size="sm"
						onClick={() => setNewPlaylistOpen(true)}
					>
						<Plus className="h-4 w-4 mr-1" />
						New playlist
					</Button>
				</div>

				{playlistsPending && (
					<div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
						{Array.from({ length: 4 }).map((_, i) => (
							// biome-ignore lint/suspicious/noArrayIndexKey: skeleton placeholders have no identity
							<div
								key={i}
								className="overflow-hidden rounded-lg border border-border"
							>
								<Skeleton className="aspect-video w-full" />
								<div className="p-3 space-y-2">
									<Skeleton className="h-4 w-full" />
									<Skeleton className="h-3 w-2/3" />
								</div>
							</div>
						))}
					</div>
				)}

				{!playlistsPending && playlists.length === 0 && (
					<p className="py-8 text-center text-muted-foreground">
						No personal playlists yet.
					</p>
				)}

				{!playlistsPending && playlists.length > 0 && (
					<>
						<div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
							{playlists.map((playlist) => (
								<div key={playlist.id} className="relative group">
									<PlaylistCard playlist={playlist} />
									<Button
										variant="ghost"
										size="icon"
										className="absolute top-2 right-2 h-7 w-7 opacity-0 group-hover:opacity-100 transition-opacity text-muted-foreground hover:text-destructive"
										disabled={deletePlaylist.isPending}
										onClick={() =>
											deletePlaylist.mutate(playlist.id, {
												onSuccess: () => refetch(),
											})
										}
										aria-label="Delete playlist"
									>
										<Trash2 className="h-3.5 w-3.5" />
									</Button>
								</div>
							))}
						</div>

						{hasNextPage && (
							<div className="mt-2 flex justify-center">
								<Button
									variant="outline"
									onClick={() => fetchNextPage()}
									disabled={isFetchingNextPage}
								>
									{isFetchingNextPage ? "Loading…" : "Load more"}
								</Button>
							</div>
						)}
					</>
				)}
			</section>

			<Dialog open={newPlaylistOpen} onOpenChange={setNewPlaylistOpen}>
				<DialogContent>
					<DialogHeader>
						<DialogTitle>New playlist</DialogTitle>
					</DialogHeader>
					<CreatePlaylistForm onSuccess={() => setNewPlaylistOpen(false)} />
				</DialogContent>
			</Dialog>
		</main>
	);
}
