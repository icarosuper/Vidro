import { createFileRoute, redirect } from '@tanstack/react-router'
import { ListVideo, Plus, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { EmptyState, ErrorState } from '#/components/StatePanel'
import { Button } from '#/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from '#/components/ui/dialog'
import { Skeleton } from '#/components/ui/skeleton'
import { CreatePlaylistForm } from '#/features/playlists/components/CreatePlaylistForm'
import { DeletePlaylistDialog } from '#/features/playlists/components/DeletePlaylistDialog'
import { PlaylistCard } from '#/features/playlists/components/PlaylistCard'
import { useDeletePlaylist, useUserPlaylists } from '#/features/playlists/hooks'
import type { PlaylistSummary } from '#/features/playlists/types'
import { useCurrentUser } from '#/features/users/hooks'
import { seo } from '#/shared/lib/seo'
import { tokenStore } from '#/shared/lib/token-store'

export const Route = createFileRoute('/dashboard')({
  beforeLoad: () => {
    const isServer = typeof window === 'undefined'
    if (isServer) return

    const isAuthenticated = tokenStore.get() !== null
    if (!isAuthenticated) {
      throw redirect({ to: '/' })
    }
  },
  head: () => ({
    meta: seo({
      title: 'Dashboard',
      description: 'Your channels, videos and playlists.',
      noIndex: true,
    }),
  }),
  component: DashboardPage,
})

function DashboardPage() {
  const { data: currentUser } = useCurrentUser()
  const [newPlaylistOpen, setNewPlaylistOpen] = useState(false)

  const {
    data: playlistsData,
    isPending: playlistsPending,
    isError: playlistsFailed,
    fetchNextPage,
    hasNextPage,
    isFetchingNextPage,
    refetch,
    isRefetching,
  } = useUserPlaylists(currentUser?.username)

  const deletePlaylist = useDeletePlaylist()
  const [playlistToDelete, setPlaylistToDelete] =
    useState<PlaylistSummary | null>(null)

  function confirmDelete() {
    if (!playlistToDelete) return
    deletePlaylist.mutate(playlistToDelete.id)
    setPlaylistToDelete(null)
  }

  const playlists = playlistsData?.pages.flatMap((page) => page.items) ?? []
  const hasNoPlaylists =
    !playlistsPending && !playlistsFailed && playlists.length === 0
  const hasPlaylists = !playlistsPending && playlists.length > 0

  return (
    <main className="page-container py-8 space-y-8">
      <h1 className="text-2xl font-bold">Dashboard</h1>

      {/* Personal playlists */}
      <section className="space-y-4">
        <div className="flex items-center justify-between">
          <h2 className="text-xs font-semibold text-muted-foreground uppercase tracking-wide">
            My playlists
            {hasPlaylists && ` (${playlists.length}${hasNextPage ? '+' : ''})`}
          </h2>
          <Button
            variant="ghost"
            size="sm"
            onClick={() => setNewPlaylistOpen(true)}
          >
            <Plus className="h-4 w-4 mr-1" />
            New playlist
          </Button>
        </div>

        {playlistsPending && (
          <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
            {Array.from({ length: 4 }).map((_, i) => (
              <div
                // biome-ignore lint/suspicious/noArrayIndexKey: skeleton placeholders have no identity
                key={i}
                className="overflow-hidden rounded-lg border border-border"
              >
                <Skeleton className="aspect-video w-full" />
                <div className="p-3 space-y-2">
                  <Skeleton className="h-4 w-full" />
                  <Skeleton className="h-3 w-2/3" />
                </div>
              </div>
            ))}
          </div>
        )}

        {playlistsFailed && (
          <ErrorState
            title="Could not load your playlists"
            onRetry={() => refetch()}
            isRetrying={isRefetching}
          />
        )}

        {hasNoPlaylists && (
          <EmptyState
            icon={ListVideo}
            title="No personal playlists yet"
            description="Playlists that belong to you, not to one of your channels."
            action={
              <Button onClick={() => setNewPlaylistOpen(true)}>
                <Plus /> New playlist
              </Button>
            }
          />
        )}

        {hasPlaylists && (
          <>
            <div className="grid grid-cols-2 gap-4 lg:grid-cols-4">
              {playlists.map((playlist) => (
                <div key={playlist.id} className="relative group">
                  <PlaylistCard playlist={playlist} />
                  <Button
                    variant="ghost"
                    size="icon"
                    className="absolute top-4 right-2 z-10 size-7 bg-background/90 text-muted-foreground opacity-0 transition-opacity group-hover:opacity-100 hover:text-destructive focus-visible:opacity-100"
                    disabled={deletePlaylist.isPending}
                    onClick={() => setPlaylistToDelete(playlist)}
                    aria-label="Delete playlist"
                  >
                    <Trash2 className="h-3.5 w-3.5" />
                  </Button>
                </div>
              ))}
            </div>

            {hasNextPage && (
              <div className="mt-2 flex justify-center">
                <Button
                  variant="outline"
                  onClick={() => fetchNextPage()}
                  disabled={isFetchingNextPage}
                >
                  {isFetchingNextPage ? 'Loading…' : 'Load more'}
                </Button>
              </div>
            )}
          </>
        )}
      </section>

      <DeletePlaylistDialog
        playlistName={playlistToDelete?.name ?? null}
        onCancel={() => setPlaylistToDelete(null)}
        onConfirm={confirmDelete}
      />

      <Dialog open={newPlaylistOpen} onOpenChange={setNewPlaylistOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>New playlist</DialogTitle>
          </DialogHeader>
          <CreatePlaylistForm onSuccess={() => setNewPlaylistOpen(false)} />
        </DialogContent>
      </Dialog>
    </main>
  )
}
