import { Link } from '@tanstack/react-router'
import { Film, Trash2 } from 'lucide-react'
import { Button } from '#/components/ui/button'
import { useRemoveVideoFromPlaylist } from '../hooks'
import type { PlaylistItem } from '../types'

type Props = {
  playlistId: string
  items: PlaylistItem[]
  isOwner: boolean
}

function formatDuration(seconds: number): string {
  const h = Math.floor(seconds / 3600)
  const m = Math.floor((seconds % 3600) / 60)
  const s = Math.floor(seconds % 60)
  if (h > 0)
    return `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`
  return `${m}:${String(s).padStart(2, '0')}`
}

function ItemThumbnail({ item }: { item: PlaylistItem }) {
  return (
    <div className="relative aspect-video w-32 shrink-0 overflow-hidden rounded-md bg-muted sm:w-40">
      {item.thumbnailUrl ? (
        <img
          src={item.thumbnailUrl}
          alt=""
          className="h-full w-full object-cover"
        />
      ) : (
        <div className="flex h-full w-full items-center justify-center">
          <Film className="size-5 text-muted-foreground" />
        </div>
      )}
      {item.durationSeconds !== null && (
        <span className="absolute right-1 bottom-1 rounded bg-black/70 px-1 py-0.5 font-mono text-[11px] text-white">
          {formatDuration(item.durationSeconds)}
        </span>
      )}
    </div>
  )
}

// Each row is one stretched link (the title), like VideoCard, so Remove can sit on top of it
// without nesting a button inside an anchor.
export function PlaylistItemList({ playlistId, items, isOwner }: Props) {
  const removeVideo = useRemoveVideoFromPlaylist(playlistId)

  if (items.length === 0) {
    return (
      <div className="rounded-lg border border-dashed border-border px-6 py-12 text-center">
        <p className="text-sm text-muted-foreground">
          No videos in this playlist yet.
        </p>
      </div>
    )
  }

  return (
    <ol className="space-y-1">
      {items.map((item, index) => (
        <li
          key={item.videoId}
          className="group relative flex items-center gap-3 rounded-lg p-2 transition-colors hover:bg-card hover:shadow-sm hover:ring-1 hover:ring-border has-[a:focus-visible]:ring-2 has-[a:focus-visible]:ring-ring"
        >
          <span className="w-6 shrink-0 text-center font-mono text-xs text-muted-foreground group-hover:text-primary">
            {index + 1}
          </span>

          <ItemThumbnail item={item} />

          <h3 className="line-clamp-2 min-w-0 flex-1 text-sm leading-snug font-bold">
            <Link
              to="/watch/$videoId"
              params={{ videoId: item.videoId }}
              className="text-foreground no-underline outline-none after:absolute after:inset-0 after:rounded-lg after:content-['']"
            >
              {item.title}
            </Link>
          </h3>

          {isOwner && (
            <Button
              variant="ghost"
              size="icon"
              className="relative z-10 size-8 shrink-0 text-muted-foreground transition-opacity hover:text-destructive focus-visible:opacity-100 sm:opacity-0 sm:group-hover:opacity-100"
              disabled={removeVideo.isPending}
              onClick={() => removeVideo.mutate(item.videoId)}
              aria-label="Remove from playlist"
            >
              <Trash2 className="size-4" />
            </Button>
          )}
        </li>
      ))}
    </ol>
  )
}
