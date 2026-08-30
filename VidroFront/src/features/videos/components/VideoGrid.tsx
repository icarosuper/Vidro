import { Skeleton } from '#/components/ui/skeleton'
import type { VideoSummary } from '../types'
import { VideoCard } from './VideoCard'

type VideoGridProps = {
  videos: VideoSummary[]
  isLoading?: boolean
  skeletonCount?: number
}

function VideoCardSkeleton() {
  return (
    <div className="overflow-hidden rounded-lg border border-border">
      <Skeleton className="aspect-video w-full" />
      <div className="p-3">
        <div className="flex gap-3">
          <Skeleton className="mt-0.5 h-8 w-8 shrink-0 rounded-full" />
          <div className="flex-1 space-y-2">
            <Skeleton className="h-4 w-full" />
            <Skeleton className="h-3 w-2/3" />
            <Skeleton className="h-3 w-1/2" />
          </div>
        </div>
      </div>
    </div>
  )
}

export function VideoGrid({ videos, isLoading = false, skeletonCount = 8 }: VideoGridProps) {
  if (isLoading) {
    return (
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
        {Array.from({ length: skeletonCount }).map((_, i) => (
          // biome-ignore lint/suspicious/noArrayIndexKey: skeleton placeholders have no identity
          <VideoCardSkeleton key={i} />
        ))}
      </div>
    )
  }

  if (videos.length === 0) {
    return (
      <p className="py-8 text-center text-muted-foreground">No videos found.</p>
    )
  }

  return (
    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
      {videos.map((video) => (
        <VideoCard key={video.videoId} video={video} />
      ))}
    </div>
  )
}
