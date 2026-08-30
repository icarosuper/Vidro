import { Link } from "@tanstack/react-router";
import { Lock, Video } from "lucide-react";
import { Badge } from "#/components/ui/badge";
import { Card, CardContent } from "#/components/ui/card";
import { PlaylistVisibility } from "#/shared/types";
import type { PlaylistSummary } from "../types";

type Props = {
	playlist: PlaylistSummary;
};

export function PlaylistCard({ playlist }: Props) {
	const isPrivate = playlist.visibility.id === PlaylistVisibility.Private;

	return (
		<Link
			to="/playlists/$playlistId"
			params={{ playlistId: playlist.id }}
			className="no-underline"
		>
			<Card className="overflow-hidden transition-shadow hover:shadow-md border-0 shadow-none bg-transparent rounded-xl p-3 gap-0">
				<div className="relative aspect-video w-full bg-muted rounded-xl overflow-hidden flex items-center justify-center">
					<Video className="h-8 w-8 text-muted-foreground" />
					<div className="absolute bottom-2 right-2">
						<Badge variant="secondary" className="text-xs">
							{playlist.videoCount}{" "}
							{playlist.videoCount === 1 ? "video" : "videos"}
						</Badge>
					</div>
					{isPrivate && (
						<div className="absolute top-2 left-2">
							<Badge variant="outline" className="text-xs gap-1">
								<Lock className="h-2.5 w-2.5" />
								Private
							</Badge>
						</div>
					)}
				</div>

				<CardContent className="px-3 pt-2 pb-0">
					<h3 className="line-clamp-2 text-sm font-bold leading-tight text-foreground">
						{playlist.name}
					</h3>
					{playlist.description && (
						<p className="mt-1 line-clamp-1 text-xs text-muted-foreground">
							{playlist.description}
						</p>
					)}
				</CardContent>
			</Card>
		</Link>
	);
}
