import { Link, createFileRoute } from "@tanstack/react-router";
import { Camera, Pencil, Plus, Users } from "lucide-react";
import { useRef, useState } from "react";
import { Avatar, AvatarFallback, AvatarImage } from "#/components/ui/avatar";
import { Button } from "#/components/ui/button";
import { Card, CardContent } from "#/components/ui/card";
import {
	Dialog,
	DialogContent,
	DialogHeader,
	DialogTitle,
} from "#/components/ui/dialog";
import { Separator } from "#/components/ui/separator";
import { Skeleton } from "#/components/ui/skeleton";
import { useIsAuthenticated } from "#/features/auth/hooks";
import { EditChannelForm } from "#/features/channels/components/EditChannelForm";
import { SubscribeButton } from "#/features/channels/components/SubscribeButton";
import { useChannel, useUploadChannelAvatar } from "#/features/channels/hooks";
import { CreatePlaylistForm } from "#/features/playlists/components/CreatePlaylistForm";
import { PlaylistCard } from "#/features/playlists/components/PlaylistCard";
import { useChannelPlaylists } from "#/features/playlists/hooks";
import { VideoCard } from "#/features/videos/components/VideoCard";
import { useChannelVideos } from "#/features/videos/hooks";
import { useCurrentUser } from "#/features/users/hooks";

export const Route = createFileRoute("/$username/$channel")({
	component: ChannelPage,
});

function ChannelHeaderSkeleton() {
	return (
		<Card>
			<CardContent className="p-6">
				<div className="flex items-start gap-6">
					<Skeleton className="h-24 w-24 shrink-0 rounded-lg" />
					<div className="flex-1 space-y-3">
						<Skeleton className="h-6 w-1/3" />
						<Skeleton className="h-4 w-full" />
						<Skeleton className="h-4 w-2/3" />
						<Skeleton className="h-4 w-1/4" />
					</div>
				</div>
			</CardContent>
		</Card>
	);
}

function ChannelPage() {
	const { username, channel: handle } = Route.useParams();
	const isAuthenticated = useIsAuthenticated();
	const [editOpen, setEditOpen] = useState(false);
	const avatarInputRef = useRef<HTMLInputElement>(null);

	const {
		data: channel,
		isPending: channelPending,
		isError,
		error,
	} = useChannel(username, handle);
	const { data: currentUser } = useCurrentUser();
	const uploadAvatar = useUploadChannelAvatar(username, handle);
	const {
		data: videosData,
		isPending: videosPending,
		fetchNextPage,
		hasNextPage,
		isFetchingNextPage,
	} = useChannelVideos(username, handle);

	const isOwner =
		isAuthenticated &&
		!!currentUser &&
		!!channel &&
		currentUser.userId === channel.ownerId;
	const canSubscribe = isAuthenticated && !isOwner;
	const [newPlaylistOpen, setNewPlaylistOpen] = useState(false);

	function handleAvatarFileChange(e: React.ChangeEvent<HTMLInputElement>) {
		const file = e.target.files?.[0];
		if (!file) return;
		uploadAvatar.mutate(file);
		e.target.value = "";
	}

	const {
		data: playlistsData,
		isPending: playlistsPending,
		fetchNextPage: fetchNextPlaylistsPage,
		hasNextPage: hasNextPlaylistsPage,
		isFetchingNextPage: isFetchingNextPlaylistsPage,
	} = useChannelPlaylists(username, handle);

	const videos = videosData?.pages.flatMap((page) => page.videos) ?? [];
	const playlists = playlistsData?.pages.flatMap((page) => page.items) ?? [];
	const channelInitial = channel?.name.charAt(0).toUpperCase() ?? "?";
	const ownerInitial = channel?.ownerUsername.charAt(0).toUpperCase() ?? "?";
	const followerCount = channel?.followerCount ?? 0;
	const followerLabel =
		followerCount === 1 ? "1 follower" : `${followerCount} followers`;

	if (isError) {
		return (
			<main className="page-container py-8">
				<p className="text-destructive">{error.message}</p>
			</main>
		);
	}

	return (
		<main className="page-container py-8 space-y-6">
			{/* Channel header */}
			{channelPending ? (
				<ChannelHeaderSkeleton />
			) : (
				<Card>
					<CardContent className="p-6">
						<div className="flex items-start gap-6">
							{/* Large channel avatar — clickable for owner */}
							<div className="relative shrink-0">
								<Avatar className="h-24 w-24 rounded-lg">
									<AvatarImage
										src={channel.avatarUrl ?? undefined}
										alt={channel.name}
									/>
									<AvatarFallback className="text-3xl rounded-lg">
										{channelInitial}
									</AvatarFallback>
								</Avatar>
								{isOwner && (
									<>
										<button
											type="button"
											onClick={() => avatarInputRef.current?.click()}
											disabled={uploadAvatar.isPending}
											aria-label="Change avatar"
											className="absolute inset-0 flex items-center justify-center rounded-lg bg-black/50 opacity-0 hover:opacity-100 transition-opacity disabled:cursor-not-allowed"
										>
											<Camera className="h-6 w-6 text-white" />
										</button>
										<input
											ref={avatarInputRef}
											type="file"
											accept="image/*"
											className="hidden"
											onChange={handleAvatarFileChange}
										/>
									</>
								)}
							</div>

							<div className="min-w-0 flex-1">
								{/* Name + edit + subscribe */}
								<div className="flex items-center gap-2">
									<h1 className="text-2xl font-bold leading-tight">
										{channel.name}
									</h1>
									{isOwner && (
										<Button
											variant="ghost"
											size="icon"
											onClick={() => setEditOpen(true)}
											aria-label="Edit channel"
										>
											<Pencil className="h-4 w-4" />
										</Button>
									)}
									{canSubscribe && (
										<SubscribeButton
											username={username}
											handle={handle}
											initialIsFollowing={channel.isFollowing}
										/>
									)}
								</div>
								<p className="text-sm text-muted-foreground">
									@{channel.ownerUsername}/{channel.handle}
								</p>

								{/* Description */}
								{channel.description && (
									<p className="mt-1 text-sm text-muted-foreground leading-relaxed">
										{channel.description}
									</p>
								)}

								{/* Follower count */}
								<p className="mt-2 flex items-center gap-1.5 text-sm text-muted-foreground">
									<Users className="h-4 w-4" />
									{followerLabel}
								</p>

								{/* Owner info */}
								<Separator className="my-3" />
								<Link
									to="/$username"
									params={{ username: channel.ownerUsername }}
									className="no-underline inline-flex items-center gap-2 group"
								>
									<Avatar className="h-7 w-7 shrink-0">
										<AvatarImage
											src={channel.ownerAvatarUrl ?? undefined}
											alt={channel.ownerUsername}
										/>
										<AvatarFallback className="text-xs">
											{ownerInitial}
										</AvatarFallback>
									</Avatar>
									<span className="text-sm text-muted-foreground group-hover:text-foreground transition-colors">
										By {channel.ownerUsername}
									</span>
								</Link>
							</div>
						</div>
					</CardContent>
				</Card>
			)}

			{/* Videos */}
			<section>
				<h2 className="mb-4 text-lg font-semibold text-muted-foreground uppercase tracking-wide text-xs">
					Videos
					{!videosPending &&
						videos.length > 0 &&
						` (${videos.length}${hasNextPage ? "+" : ""})`}
				</h2>

				{videosPending && (
					<div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
						{Array.from({ length: 8 }).map((_, i) => (
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

				{!videosPending && videos.length === 0 && (
					<p className="py-8 text-center text-muted-foreground">
						No videos yet.
					</p>
				)}

				{!videosPending && videos.length > 0 && (
					<>
						<div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
							{videos.map((video) => (
								<VideoCard
									key={video.videoId}
									video={video}
									hideChannelInfo
									isOwner={isOwner}
								/>
							))}
						</div>

						{hasNextPage && (
							<div className="mt-6 flex justify-center">
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

			{/* Playlists */}
			<section>
				<div className="mb-4 flex items-center justify-between">
					<h2 className="text-lg font-semibold text-muted-foreground uppercase tracking-wide text-xs">
						Playlists
						{!playlistsPending &&
							playlists.length > 0 &&
							` (${playlists.length}${hasNextPlaylistsPage ? "+" : ""})`}
					</h2>
					{isOwner && channel && (
						<Button
							variant="ghost"
							size="sm"
							onClick={() => setNewPlaylistOpen(true)}
						>
							<Plus className="h-4 w-4 mr-1" />
							New playlist
						</Button>
					)}
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
						No playlists yet.
					</p>
				)}

				{!playlistsPending && playlists.length > 0 && (
					<>
						<div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
							{playlists.map((playlist) => (
								<PlaylistCard key={playlist.id} playlist={playlist} />
							))}
						</div>

						{hasNextPlaylistsPage && (
							<div className="mt-6 flex justify-center">
								<Button
									variant="outline"
									onClick={() => fetchNextPlaylistsPage()}
									disabled={isFetchingNextPlaylistsPage}
								>
									{isFetchingNextPlaylistsPage ? "Loading…" : "Load more"}
								</Button>
							</div>
						)}
					</>
				)}
			</section>

			{channel && (
				<Dialog open={editOpen} onOpenChange={setEditOpen}>
					<DialogContent>
						<DialogHeader>
							<DialogTitle>Edit channel</DialogTitle>
						</DialogHeader>
						<EditChannelForm
							channel={channel}
							onSuccess={() => setEditOpen(false)}
						/>
					</DialogContent>
				</Dialog>
			)}

			{channel && (
				<Dialog open={newPlaylistOpen} onOpenChange={setNewPlaylistOpen}>
					<DialogContent>
						<DialogHeader>
							<DialogTitle>New playlist</DialogTitle>
						</DialogHeader>
						<CreatePlaylistForm
							channelId={channel.channelId}
							onSuccess={() => setNewPlaylistOpen(false)}
						/>
					</DialogContent>
				</Dialog>
			)}
		</main>
	);
}
