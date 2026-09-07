import { createFileRoute } from '@tanstack/react-router'
import { Button } from '#/components/ui/button'
import { VideoGrid } from '#/features/videos/components/VideoGrid'
import { useSearchVideos } from '#/features/videos/hooks'

type SearchParams = { q: string }

export const Route = createFileRoute('/search')({
  validateSearch: (search: Record<string, unknown>): SearchParams => ({
    q: typeof search.q === 'string' ? search.q : '',
  }),
  component: SearchPage,
})

function SearchPage() {
  const { q } = Route.useSearch()
  const {
    data,
    isPending,
    isError,
    fetchNextPage,
    hasNextPage,
    isFetchingNextPage,
  } = useSearchVideos(q)

  const hasQuery = q.length > 0
  if (!hasQuery) {
    return (
      <main className="page-container py-8">
        <p className="py-8 text-center text-muted-foreground">
          Type something in the search box to find videos.
        </p>
      </main>
    )
  }

  if (isError) {
    return (
      <main className="page-container py-8">
        <p className="py-8 text-center text-muted-foreground">
          Could not load the search results. Try again.
        </p>
      </main>
    )
  }

  const matchingVideos = data?.pages.flatMap((page) => page.videos) ?? []

  return (
    <main className="page-container py-8">
      <h1 className="mb-4 text-xl font-semibold">Results for “{q}”</h1>
      <VideoGrid videos={matchingVideos} isLoading={isPending} />
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
    </main>
  )
}
