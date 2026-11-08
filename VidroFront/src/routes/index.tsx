import { createFileRoute } from '@tanstack/react-router'
import { Button } from '#/components/ui/button'
import { useIsAuthenticated } from '#/features/auth/hooks'
import { getTrending } from '#/features/videos/api'
import { VideoGrid } from '#/features/videos/components/VideoGrid'
import { useFeed, useTrending, videoKeys } from '#/features/videos/hooks'
import { seo } from '#/shared/lib/seo'

export const Route = createFileRoute('/')({
  head: () => ({
    meta: seo({
      title: 'Vidro',
      description: 'Trending videos and your feed on Vidro.',
    }),
  }),
  loader: async ({ context: { queryClient } }) => {
    await queryClient.prefetchQuery({
      queryKey: videoKeys.trending(),
      queryFn: () => getTrending(20),
    })
  },
  component: HomePage,
})

function FeedSection() {
  const {
    data,
    isPending,
    isError,
    fetchNextPage,
    hasNextPage,
    isFetchingNextPage,
  } = useFeed(true)

  const feedVideos = data?.pages.flatMap((page) => page.videos) ?? []

  if (isError) {
    return (
      <section>
        <h2 className="mb-5 text-2xl font-extrabold tracking-tight">
          Your Feed
        </h2>
        <p className="py-8 text-center text-muted-foreground">
          Could not load your feed. Try again.
        </p>
      </section>
    )
  }

  return (
    <section>
      <h2 className="mb-5 text-2xl font-extrabold tracking-tight">Your Feed</h2>
      <VideoGrid videos={feedVideos} isLoading={isPending} featureFirst />
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

function TrendingSection({ featureFirst }: { featureFirst: boolean }) {
  const { data, isPending, isError } = useTrending()
  const videos = data?.videos ?? []

  return (
    <section>
      <h2 className="mb-5 text-2xl font-extrabold tracking-tight">Trending</h2>
      {isError ? (
        <p className="py-8 text-center text-muted-foreground">
          Could not load trending videos. Try again.
        </p>
      ) : (
        <VideoGrid
          videos={videos}
          isLoading={isPending}
          featureFirst={featureFirst}
        />
      )}
    </section>
  )
}

// Signed in with no follows, the feed is empty — then it does not take the top of the page, and
// Trending becomes the first section. The first section always features its first card.
function useShowsFeed(isAuthenticated: boolean): boolean {
  const feed = useFeed(isAuthenticated)
  const feedIsEmpty =
    feed.isSuccess && feed.data.pages.every((page) => page.videos.length === 0)
  return isAuthenticated && !feedIsEmpty
}

function HomePage() {
  const isAuthenticated = useIsAuthenticated()
  const showsFeed = useShowsFeed(isAuthenticated)

  return (
    <main className="page-container py-8 space-y-10">
      {showsFeed && <FeedSection />}
      <TrendingSection featureFirst={!showsFeed} />
    </main>
  )
}
