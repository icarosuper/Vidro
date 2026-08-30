import { createFileRoute } from '@tanstack/react-router'
import { Button } from '#/components/ui/button'
import { VideoGrid } from '#/features/videos/components/VideoGrid'
import { useFeed, useTrending, videoKeys } from '#/features/videos/hooks'
import { getTrending } from '#/features/videos/api'
import { useIsAuthenticated } from '#/features/auth/hooks'

export const Route = createFileRoute('/')({
  loader: async ({ context: { queryClient } }) => {
    await queryClient.prefetchQuery({
      queryKey: videoKeys.trending(),
      queryFn: () => getTrending(20),
    })
  },
  component: HomePage,
})

function FeedSection() {
  const { data, isPending, fetchNextPage, hasNextPage, isFetchingNextPage } = useFeed(true)

  const feedVideos = data?.pages.flatMap((page) => page.videos) ?? []

  return (
    <section>
      <h2 className="mb-4 text-xl font-semibold">Your Feed</h2>
      <VideoGrid videos={feedVideos} isLoading={isPending} />
      {hasNextPage && (
        <div className="mt-6 flex justify-center">
          <Button
            variant="outline"
            onClick={() => fetchNextPage()}
            disabled={isFetchingNextPage}
          >
            {isFetchingNextPage ? 'Loading…' : 'Load more'}
          </Button>
        </div>
      )}
    </section>
  )
}

function TrendingSection() {
  const { data, isPending } = useTrending()
  const videos = data?.videos ?? []

  return (
    <section>
      <h2 className="mb-4 text-xl font-semibold">Trending</h2>
      <VideoGrid videos={videos} isLoading={isPending} />
    </section>
  )
}

function HomePage() {
  const isAuthenticated = useIsAuthenticated()

  return (
    <main className="page-container py-8 space-y-10">
      {isAuthenticated && <FeedSection />}
      <TrendingSection />
    </main>
  )
}
