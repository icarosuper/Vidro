import { Skeleton } from '#/components/ui/skeleton'
import { VideoCard, type VideoCardVideo } from './VideoCard'

type VideoGridProps = {
  videos: VideoCardVideo[]
  isLoading?: boolean
  skeletonCount?: number
  // Makes the first card span 2×2 (from sm up). Use it on the first grid of a page only.
  featureFirst?: boolean
  // Passed to every card — the channel page, where the channel is the page itself.
  hideChannelInfo?: boolean
  isOwner?: boolean
  emptyMessage?: string
}

// Shared with the playlist grid, so both card kinds line up the same way on every page.
export const CARD_GRID_CLASS =
  'grid grid-cols-1 gap-x-4 gap-y-8 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4'

function VideoCardSkeleton() {
  return (
    <div>
      <Skeleton className="aspect-video w-full rounded-md" />
      <Skeleton className="mt-3 h-4 w-11/12" />
      <div className="mt-2 flex items-center gap-2">
        <Skeleton className="size-5 rounded-full" />
        <Skeleton className="h-3 w-24" />
        <Skeleton className="ml-auto h-3 w-16" />
      </div>
    </div>
  )
}

export function VideoGrid({
  videos,
  isLoading = false,
  skeletonCount = 8,
  featureFirst = false,
  hideChannelInfo = false,
  isOwner = false,
  emptyMessage = 'No videos found.',
}: VideoGridProps) {
  if (isLoading) {
    return (
      <div className={CARD_GRID_CLASS}>
        {Array.from({ length: skeletonCount }).map((_, i) => (
          // biome-ignore lint/suspicious/noArrayIndexKey: skeleton placeholders have no identity
          <VideoCardSkeleton key={i} />
        ))}
      </div>
    )
  }

  if (videos.length === 0) {
    return (
      <p className="py-8 text-center text-muted-foreground">{emptyMessage}</p>
    )
  }

  return (
    <div className={CARD_GRID_CLASS}>
      {videos.map((video, index) => (
        <VideoCard
          key={video.videoId}
          video={video}
          isFeatured={featureFirst && index === 0}
          hideChannelInfo={hideChannelInfo}
          isOwner={isOwner}
        />
      ))}
    </div>
  )
}
