import { createFileRoute } from '@tanstack/react-router'
import { Search, SearchX } from 'lucide-react'
import { EmptyState, ErrorState } from '#/components/StatePanel'
import { Button } from '#/components/ui/button'
import { VideoGrid } from '#/features/videos/components/VideoGrid'
import { useSearchVideos } from '#/features/videos/hooks'
import { seo } from '#/shared/lib/seo'

type SearchParams = { q: string }

export const Route = createFileRoute('/search')({
  validateSearch: (search: Record<string, unknown>): SearchParams => ({
    q: typeof search.q === 'string' ? search.q : '',
  }),
  head: ({ match }) => ({
    meta: seo({
      title: match.search.q ? `Search: ${match.search.q}` : 'Search',
      description: 'Search videos on Vidro.',
      noIndex: true,
    }),
  }),
  component: SearchPage,
})

function SearchPage() {
  const { q } = Route.useSearch()
  const {
    data,
    isPending,
    isError,
    refetch,
    isRefetching,
    fetchNextPage,
    hasNextPage,
    isFetchingNextPage,
  } = useSearchVideos(q)

  const hasQuery = q.length > 0
  if (!hasQuery) {
    return (
      <main className="page-container py-8">
        <EmptyState
          icon={Search}
          title="Search Vidro"
          description="Type something in the search box to find videos. Press / to jump to it."
        />
      </main>
    )
  }

  if (isError) {
    return (
      <main className="page-container py-8">
        <ErrorState
          title="Could not load the results"
          onRetry={() => refetch()}
          isRetrying={isRefetching}
        />
      </main>
    )
  }

  const matchingVideos = data?.pages.flatMap((page) => page.videos) ?? []

  return (
    <main className="page-container py-8">
      <h1 className="mb-5 text-2xl font-extrabold tracking-tight">
        Results for “{q}”
      </h1>
      <VideoGrid
        videos={matchingVideos}
        isLoading={isPending}
        emptyState={
          <EmptyState
            icon={SearchX}
            title={`No results for “${q}”`}
            description="Try other words, or fewer of them."
          />
        }
      />
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
