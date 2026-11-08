import { Link } from '@tanstack/react-router'
import { Skeleton } from '#/components/ui/skeleton'
import { formatCompactAge, formatCount, type VideoCardVideo } from './VideoCard'

const UP_NEXT_LIMIT = 8

// The rest of the channel first (what the viewer is already watching), then trending to fill the
// list. Never the video on screen, never the same video twice, and only what anyone may watch —
// the owner's channel listing also carries their private and still-processing videos.
export function pickUpNextVideos(
  currentVideoId: string,
  channelVideos: VideoCardVideo[],
  trendingVideos: VideoCardVideo[],
  limit = UP_NEXT_LIMIT,
): VideoCardVideo[] {
  const seenVideoIds = new Set([currentVideoId])
  const picked: VideoCardVideo[] = []
  for (const candidate of [...channelVideos, ...trendingVideos]) {
    const isWatchable =
      (candidate.status === undefined || candidate.status.value === 'Ready') &&
      (candidate.visibility === undefined ||
        candidate.visibility.value === 'Public')
    if (!isWatchable || seenVideoIds.has(candidate.videoId)) continue
    seenVideoIds.add(candidate.videoId)
    picked.push(candidate)
    if (picked.length === limit) break
  }
  return picked
}

function UpNextItem({ video }: { video: VideoCardVideo }) {
  const thumbnailUrl = video.thumbnailUrls[0]
  return (
    <li className="group relative flex gap-3 rounded-md p-1.5 transition-colors hover:bg-card has-[a:focus-visible]:ring-2 has-[a:focus-visible]:ring-ring">
      <div className="aspect-video w-40 shrink-0 overflow-hidden rounded bg-muted">
        {thumbnailUrl && (
          <img
            src={thumbnailUrl}
            alt=""
            className="h-full w-full object-cover"
          />
        )}
      </div>
      <div className="min-w-0 py-0.5">
        <h3 className="line-clamp-2 text-sm leading-snug font-bold">
          {/* Stretched over the item, like VideoCard's title. */}
          <Link
            to="/watch/$videoId"
            params={{ videoId: video.videoId }}
            className="text-foreground no-underline outline-none after:absolute after:inset-0 after:content-['']"
          >
            {video.title}
          </Link>
        </h3>
        <p className="mt-1 truncate text-xs font-semibold text-muted-foreground">
          {video.channelName ?? video.channelHandle}
        </p>
        <p className="font-mono text-xs text-muted-foreground">
          {formatCount(video.viewCount)} views ·{' '}
          {formatCompactAge(video.createdAt)}
        </p>
      </div>
    </li>
  )
}

type UpNextListProps = {
  videos: VideoCardVideo[]
  isLoading: boolean
}

export function UpNextList({ videos, isLoading }: UpNextListProps) {
  if (isLoading) {
    return (
      <div className="space-y-3">
        {Array.from({ length: 4 }).map((_, i) => (
          // biome-ignore lint/suspicious/noArrayIndexKey: skeleton placeholders have no identity
          <div key={i} className="flex gap-3 p-1.5">
            <Skeleton className="aspect-video w-40 rounded" />
            <div className="flex-1 space-y-2 py-1">
              <Skeleton className="h-3.5 w-full" />
              <Skeleton className="h-3 w-2/3" />
            </div>
          </div>
        ))}
      </div>
    )
  }

  if (videos.length === 0) {
    return (
      <p className="text-sm text-muted-foreground">
        Nothing else to watch yet.
      </p>
    )
  }

  return (
    <ul className="space-y-1">
      {videos.map((video) => (
        <UpNextItem key={video.videoId} video={video} />
      ))}
    </ul>
  )
}
