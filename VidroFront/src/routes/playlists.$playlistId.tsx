import { createFileRoute, Link } from '@tanstack/react-router'
import { ListVideo, Lock, Pencil, Play, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { QueryErrorState } from '#/components/StatePanel'
import { Button } from '#/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from '#/components/ui/dialog'
import { Skeleton } from '#/components/ui/skeleton'
import { getPlaylist } from '#/features/playlists/api'
import { DeletePlaylistDialog } from '#/features/playlists/components/DeletePlaylistDialog'
import { EditPlaylistForm } from '#/features/playlists/components/EditPlaylistForm'
import { PlaylistItemList } from '#/features/playlists/components/PlaylistItemList'
import {
  playlistKeys,
  useDeletePlaylist,
  usePlaylist,
} from '#/features/playlists/hooks'
import { fetchPlaylistSsr } from '#/features/playlists/server'
import type { Playlist } from '#/features/playlists/types'
import { formatCompactAge } from '#/features/videos/components/VideoCard'
import { seo } from '#/shared/lib/seo'
import { PlaylistVisibility } from '#/shared/types'

export const Route = createFileRoute('/playlists/$playlistId')({
  loader: async ({ params, context: { queryClient, accessToken } }) => {
    await queryClient.prefetchQuery({
      queryKey: playlistKeys.detail(params.playlistId),
      queryFn: () =>
        typeof window === 'undefined'
          ? fetchPlaylistSsr(params.playlistId, accessToken)
          : getPlaylist(params.playlistId),
    })

    const playlist = queryClient.getQueryData<Playlist>(
      playlistKeys.detail(params.playlistId),
    )
    return { playlist }
  },
  head: ({ loaderData }) => {
    const playlist = loaderData?.playlist
    if (!playlist) return {}

    return {
      meta: seo({
        title: playlist.name,
        description:
          playlist.description ??
          `A playlist with ${playlist.videoCount} ${playlist.videoCount === 1 ? 'video' : 'videos'} on Vidro.`,
      }),
    }
  },
  component: PlaylistPage,
})

// The panel sits beside the list from lg up and stays put while the list scrolls; below lg it
// stacks on top. Top offset = the sticky top bar (h-14) plus the page's own py-6.
const PANEL_CLASS =
  'overflow-hidden rounded-xl border border-border bg-card lg:sticky lg:top-20'

function PlaylistPageSkeleton() {
  return (
    <main className="page-container py-6">
      <div className="grid gap-8 lg:grid-cols-[22rem_minmax(0,1fr)] lg:items-start">
        <div className={PANEL_CLASS}>
          <Skeleton className="aspect-video w-full rounded-none" />
          <div className="space-y-3 p-5">
            <Skeleton className="h-7 w-2/3" />
            <Skeleton className="h-3.5 w-1/2" />
            <Skeleton className="h-9 w-28" />
          </div>
        </div>
        <div className="space-y-1">
          {Array.from({ length: 6 }).map((_, i) => (
            <div
              // biome-ignore lint/suspicious/noArrayIndexKey: skeleton placeholders have no identity
              key={i}
              className="flex items-center gap-3 p-2"
            >
              <Skeleton className="h-3 w-6" />
              <Skeleton className="aspect-video w-32 rounded-md sm:w-40" />
              <Skeleton className="h-4 flex-1" />
            </div>
          ))}
        </div>
      </div>
    </main>
  )
}

// A playlist has no cover of its own: it borrows the first video's thumbnail, or falls back to
// the same accent gradient as the channel cover.
function PlaylistCover({ playlist }: { playlist: Playlist }) {
  const coverUrl = playlist.items[0]?.thumbnailUrl ?? null
  const videoCountLabel =
    playlist.videoCount === 1 ? '1 video' : `${playlist.videoCount} videos`

  return (
    <div className="relative aspect-video w-full overflow-hidden bg-linear-to-br from-primary/60 via-primary/25 to-accent">
      {coverUrl ? (
        <img src={coverUrl} alt="" className="h-full w-full object-cover" />
      ) : (
        <div className="flex h-full w-full items-center justify-center">
          <ListVideo className="size-10 text-primary-foreground/80" />
        </div>
      )}
      <span className="absolute right-2 bottom-2 rounded bg-black/70 px-1.5 py-0.5 font-mono text-[11px] text-white">
        {videoCountLabel}
      </span>
    </div>
  )
}

function PlaylistPage() {
  const { playlistId } = Route.useParams()
  const navigate = Route.useNavigate()
  const {
    data: playlist,
    isPending,
    isError,
    error,
    refetch,
    isRefetching,
  } = usePlaylist(playlistId)
  const deletePlaylist = useDeletePlaylist()
  const [editOpen, setEditOpen] = useState(false)
  const [deleteOpen, setDeleteOpen] = useState(false)

  if (isPending) return <PlaylistPageSkeleton />
  if (isError) {
    return (
      <main className="page-container py-8">
        <QueryErrorState
          error={error}
          notFoundTitle="Playlist not found"
          onRetry={() => refetch()}
          isRetrying={isRefetching}
        />
      </main>
    )
  }

  const { isOwner } = playlist
  const isPrivate = playlist.visibility.id === PlaylistVisibility.Private
  const firstVideo = playlist.items[0]

  function handleDelete() {
    deletePlaylist.mutate(playlistId, {
      onSuccess: () => navigate({ to: '/' }),
    })
  }

  return (
    <main className="page-container py-6">
      <div className="grid gap-8 lg:grid-cols-[22rem_minmax(0,1fr)] lg:items-start">
        <section className={PANEL_CLASS}>
          <PlaylistCover playlist={playlist} />

          <div className="space-y-4 p-5">
            <div>
              <h1 className="text-2xl leading-tight font-extrabold tracking-tight">
                {playlist.name}
              </h1>
              <p className="mt-1.5 flex items-center gap-1.5 font-mono text-xs text-muted-foreground">
                {isPrivate && <Lock className="size-3" />}
                {playlist.visibility.value.toLowerCase()} · playlist ·{' '}
                {formatCompactAge(playlist.createdAt)}
              </p>
            </div>

            {playlist.description && (
              <p className="text-sm leading-relaxed text-foreground/90">
                {playlist.description}
              </p>
            )}

            <div className="flex items-center gap-2">
              {firstVideo && (
                <Button asChild>
                  <Link
                    to="/watch/$videoId"
                    params={{ videoId: firstVideo.videoId }}
                    className="no-underline"
                  >
                    <Play /> Play all
                  </Link>
                </Button>
              )}
              {isOwner && (
                <>
                  <Button
                    variant="ghost"
                    size="icon"
                    onClick={() => setEditOpen(true)}
                    aria-label="Edit playlist"
                  >
                    <Pencil />
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon"
                    className="text-muted-foreground hover:text-destructive"
                    disabled={deletePlaylist.isPending}
                    onClick={() => setDeleteOpen(true)}
                    aria-label="Delete playlist"
                  >
                    <Trash2 />
                  </Button>
                </>
              )}
            </div>
          </div>
        </section>

        <section aria-label="Videos">
          <PlaylistItemList
            playlistId={playlistId}
            items={playlist.items}
            isOwner={isOwner}
          />
        </section>
      </div>

      {isOwner && (
        <Dialog open={editOpen} onOpenChange={setEditOpen}>
          <DialogContent>
            <DialogHeader>
              <DialogTitle>Edit playlist</DialogTitle>
            </DialogHeader>
            <EditPlaylistForm
              playlist={playlist}
              onSuccess={() => setEditOpen(false)}
            />
          </DialogContent>
        </Dialog>
      )}

      {isOwner && (
        <DeletePlaylistDialog
          playlistName={deleteOpen ? playlist.name : null}
          onCancel={() => setDeleteOpen(false)}
          onConfirm={handleDelete}
        />
      )}
    </main>
  )
}
