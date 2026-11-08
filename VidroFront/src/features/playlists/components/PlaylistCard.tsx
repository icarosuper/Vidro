import { Link } from '@tanstack/react-router'
import { ListVideo, Lock } from 'lucide-react'
import { formatCompactAge } from '#/features/videos/components/VideoCard'
import { PlaylistVisibility } from '#/shared/types'
import type { PlaylistSummary } from '../types'

type Props = {
  playlist: PlaylistSummary
}

// Same frame as VideoCard (hover surface, stretched title link) so both line up in one grid. A
// playlist has no thumbnail of its own, so the tile is a stack of cards with the video count.
export function PlaylistCard({ playlist }: Props) {
  const isPrivate = playlist.visibility.id === PlaylistVisibility.Private
  const videoCountLabel =
    playlist.videoCount === 1 ? '1 video' : `${playlist.videoCount} videos`

  return (
    <article className="group relative flex flex-col rounded-lg transition-colors sm:-m-2 sm:p-2 hover:bg-card hover:shadow-sm hover:ring-1 hover:ring-border has-[a:focus-visible]:ring-2 has-[a:focus-visible]:ring-ring">
      <div className="relative pt-2">
        {/* The two cards peeking out behind the tile. */}
        <div className="absolute inset-x-4 top-0 h-4 rounded-t-md bg-muted/50" />
        <div className="absolute inset-x-2 top-1 h-4 rounded-t-md bg-muted/80" />
        <div className="relative flex aspect-video w-full items-center justify-center rounded-md bg-muted">
          <ListVideo className="size-8 text-muted-foreground transition-colors group-hover:text-primary" />
          <span className="absolute right-2 bottom-2 rounded bg-black/70 px-1.5 py-0.5 font-mono text-[11px] text-white">
            {videoCountLabel}
          </span>
          {isPrivate && (
            <span className="absolute top-2 left-2 flex items-center gap-1 rounded bg-background/90 px-1.5 py-0.5 text-xs font-semibold">
              <Lock className="size-3" />
              Private
            </span>
          )}
        </div>
      </div>

      <div className="mt-2.5 px-0.5">
        <h3 className="line-clamp-2 text-sm leading-snug font-bold">
          <Link
            to="/playlists/$playlistId"
            params={{ playlistId: playlist.id }}
            className="text-foreground no-underline outline-none after:absolute after:inset-0 after:rounded-lg after:content-['']"
          >
            {playlist.name}
          </Link>
        </h3>
        <p className="mt-1 flex items-center gap-2 text-xs text-muted-foreground">
          {playlist.description && (
            <span className="truncate">{playlist.description}</span>
          )}
          <span className="ml-auto shrink-0 font-mono">
            playlist · {formatCompactAge(playlist.createdAt)}
          </span>
        </p>
      </div>
    </article>
  )
}
