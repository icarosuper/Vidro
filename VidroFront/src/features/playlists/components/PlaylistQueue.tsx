import { Link } from '@tanstack/react-router'
import { Film, ListVideo } from 'lucide-react'
import { cn } from '#/lib/utils'
import type { Playlist, PlaylistItem } from '../types'

// The video after the current one, or null on the last one (or when the current video is not
// in the playlist at all). What the watch page plays when a video ends.
export function findNextInPlaylist(
  playlist: Playlist,
  currentVideoId: string,
): PlaylistItem | null {
  const currentIndex = playlist.items.findIndex(
    (item) => item.videoId === currentVideoId,
  )
  const isInPlaylist = currentIndex !== -1
  if (!isInPlaylist) return null
  return playlist.items[currentIndex + 1] ?? null
}

type Props = {
  playlist: Playlist
  currentVideoId: string
}

// The watch page's right column while playing a playlist (`?list=`): the playlist in order, the
// current video marked, every link keeping `?list=` so the queue carries on.
export function PlaylistQueue({ playlist, currentVideoId }: Props) {
  const currentIndex = playlist.items.findIndex(
    (item) => item.videoId === currentVideoId,
  )
  const position = `${currentIndex + 1} / ${playlist.items.length}`

  return (
    <section
      aria-labelledby="playlist-queue-title"
      className="overflow-hidden rounded-lg border border-border bg-card"
    >
      <header className="border-b border-border px-4 py-3">
        <p className="flex items-center gap-1.5 font-mono text-xs tracking-widest text-muted-foreground uppercase">
          <ListVideo className="size-3.5" /> Playlist · {position}
        </p>
        <h2
          id="playlist-queue-title"
          className="mt-1 truncate text-base font-extrabold tracking-tight"
        >
          <Link
            to="/playlists/$playlistId"
            params={{ playlistId: playlist.playlistId }}
            className="text-foreground no-underline hover:text-primary"
          >
            {playlist.name}
          </Link>
        </h2>
      </header>
      <ol className="max-h-[28rem] space-y-0.5 overflow-y-auto p-1.5">
        {playlist.items.map((item, index) => (
          <QueueItem
            key={item.videoId}
            item={item}
            index={index}
            playlistId={playlist.playlistId}
            isCurrent={item.videoId === currentVideoId}
          />
        ))}
      </ol>
    </section>
  )
}

type QueueItemProps = {
  item: PlaylistItem
  index: number
  playlistId: string
  isCurrent: boolean
}

function QueueItem({ item, index, playlistId, isCurrent }: QueueItemProps) {
  return (
    <li
      className={cn(
        'group relative flex items-center gap-2.5 rounded-md p-1.5 transition-colors hover:bg-muted has-[a:focus-visible]:ring-2 has-[a:focus-visible]:ring-ring',
        isCurrent && 'bg-primary/10 hover:bg-primary/10',
      )}
    >
      <span
        className={cn(
          'w-5 shrink-0 text-center font-mono text-xs',
          isCurrent ? 'text-primary' : 'text-muted-foreground',
        )}
      >
        {isCurrent ? '▶' : index + 1}
      </span>
      <div className="aspect-video w-24 shrink-0 overflow-hidden rounded bg-muted">
        {item.thumbnailUrl ? (
          <img
            src={item.thumbnailUrl}
            alt=""
            className="h-full w-full object-cover"
          />
        ) : (
          <div className="flex h-full w-full items-center justify-center">
            <Film className="size-4 text-muted-foreground" />
          </div>
        )}
      </div>
      <h3 className="line-clamp-2 min-w-0 flex-1 text-sm leading-snug font-bold">
        <Link
          to="/watch/$videoId"
          params={{ videoId: item.videoId }}
          search={{ list: playlistId }}
          aria-current={isCurrent ? 'page' : undefined}
          className="text-foreground no-underline outline-none after:absolute after:inset-0 after:content-['']"
        >
          {item.title}
        </Link>
      </h3>
    </li>
  )
}
