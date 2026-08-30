import { Link } from "@tanstack/react-router";
import { Trash2 } from "lucide-react";
import { Button } from "#/components/ui/button";
import { useRemoveVideoFromPlaylist } from "../hooks";
import type { PlaylistItem } from "../types";

type Props = {
	playlistId: string;
	items: PlaylistItem[];
	isOwner: boolean;
};

function formatDuration(seconds: number): string {
	const h = Math.floor(seconds / 3600);
	const m = Math.floor((seconds % 3600) / 60);
	const s = Math.floor(seconds % 60);
	if (h > 0)
		return `${h}:${String(m).padStart(2, "0")}:${String(s).padStart(2, "0")}`;
	return `${m}:${String(s).padStart(2, "0")}`;
}

export function PlaylistItemList({ playlistId, items, isOwner }: Props) {
	const removeVideo = useRemoveVideoFromPlaylist(playlistId);

	if (items.length === 0) {
		return (
			<p className="text-sm text-muted-foreground">
				No videos in this playlist yet.
			</p>
		);
	}

	return (
		<div className="space-y-2">
			{items.map((item, index) => (
				<div
					key={item.videoId}
					className="flex items-center gap-3 rounded-lg p-2 hover:bg-muted/50 transition-colors"
				>
					<span className="w-5 text-center text-xs text-muted-foreground shrink-0">
						{index + 1}
					</span>

					<Link
						to="/watch/$videoId"
						params={{ videoId: item.videoId }}
						className="no-underline flex items-center gap-3 flex-1 min-w-0"
					>
						<div className="relative aspect-video w-28 shrink-0 rounded overflow-hidden bg-muted">
							{item.thumbnailUrl ? (
								<img
									src={item.thumbnailUrl}
									alt={item.title}
									className="h-full w-full object-cover"
								/>
							) : (
								<div className="flex h-full w-full items-center justify-center">
									<span className="text-xs text-muted-foreground">
										No thumb
									</span>
								</div>
							)}
							{item.durationSeconds !== null && (
								<span className="absolute bottom-1 right-1 rounded bg-black/70 px-1 py-0.5 text-xs text-white">
									{formatDuration(item.durationSeconds)}
								</span>
							)}
						</div>

						<span className="line-clamp-2 text-sm font-medium text-foreground">
							{item.title}
						</span>
					</Link>

					{isOwner && (
						<Button
							variant="ghost"
							size="icon"
							className="h-7 w-7 shrink-0 text-muted-foreground hover:text-destructive"
							disabled={removeVideo.isPending}
							onClick={() => removeVideo.mutate(item.videoId)}
							aria-label="Remove from playlist"
						>
							<Trash2 className="h-3.5 w-3.5" />
						</Button>
					)}
				</div>
			))}
		</div>
	);
}
