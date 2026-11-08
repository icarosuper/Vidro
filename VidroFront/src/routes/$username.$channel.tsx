import { createFileRoute, Link } from '@tanstack/react-router'
import { Film, ListVideo, Plus, Upload } from 'lucide-react'
import { useState } from 'react'
import {
  EmptyState,
  ErrorState,
  QueryErrorState,
} from '#/components/StatePanel'
import { Button } from '#/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '#/components/ui/dialog'
import { Skeleton } from '#/components/ui/skeleton'
import { useIsAuthenticated } from '#/features/auth/hooks'
import {
  ChannelHeader,
  ChannelHeaderSkeleton,
} from '#/features/channels/components/ChannelHeader'
import { useChannel } from '#/features/channels/hooks'
import { CreatePlaylistForm } from '#/features/playlists/components/CreatePlaylistForm'
import { PlaylistCard } from '#/features/playlists/components/PlaylistCard'
import { useChannelPlaylists } from '#/features/playlists/hooks'
import { useCurrentUser } from '#/features/users/hooks'
import {
  CARD_GRID_CLASS,
  VideoGrid,
} from '#/features/videos/components/VideoGrid'
import { useChannelVideos } from '#/features/videos/hooks'
import { cn } from '#/lib/utils'
import { seo } from '#/shared/lib/seo'

type ChannelTab = 'videos' | 'playlists'

// Videos is the default and stays out of the URL; only `?tab=playlists` is spelled out, so a
// shared link or a reload opens the same tab.
type ChannelSearch = { tab?: 'playlists' }

export const Route = createFileRoute('/$username/$channel')({
  validateSearch: (search: Record<string, unknown>): ChannelSearch => ({
    tab: search.tab === 'playlists' ? 'playlists' : undefined,
  }),
  // No loader here, so the channel name is not available at head time — the handle is what the
  // URL already carries, and it is enough to tell two tabs apart.
  head: ({ params }) => ({
    meta: seo({
      title: `@${params.username}/${params.channel}`,
      description: `Videos from @${params.username}/${params.channel} on Vidro.`,
    }),
  }),
  component: ChannelPage,
})

// "4", or "20+" while more pages remain.
function formatLoadedCount(count: number, hasMore: boolean): string {
  return hasMore ? `${count}+` : String(count)
}

type TabLinkProps = {
  tab: ChannelTab
  activeTab: ChannelTab
  label: string
  count: string | null
}

function TabLink({ tab, activeTab, label, count }: TabLinkProps) {
  const isActive = tab === activeTab
  const tabSearch: ChannelSearch = tab === 'playlists' ? { tab } : {}
  return (
    <Link
      to="."
      search={tabSearch}
      replace
      aria-current={isActive ? 'page' : undefined}
      className={cn(
        '-mb-px flex items-center gap-2 border-b-2 px-1 pb-3 text-sm font-bold no-underline transition-colors',
        isActive
          ? 'border-primary text-foreground'
          : 'border-transparent text-muted-foreground hover:text-foreground',
      )}
    >
      {label}
      {count !== null && (
        <span className="rounded bg-muted px-1.5 font-mono text-xs text-muted-foreground">
          {count}
        </span>
      )}
    </Link>
  )
}

function ChannelPage() {
  const { username, channel: handle } = Route.useParams()
  const { tab } = Route.useSearch()
  const activeTab: ChannelTab = tab ?? 'videos'
  const isAuthenticated = useIsAuthenticated()
  const [newPlaylistOpen, setNewPlaylistOpen] = useState(false)

  const {
    data: channel,
    isPending: channelPending,
    isError,
    error,
    refetch,
    isRefetching,
  } = useChannel(username, handle)
  const { data: currentUser } = useCurrentUser()
  const videosQuery = useChannelVideos(username, handle)
  const playlistsQuery = useChannelPlaylists(username, handle)

  const isOwner =
    isAuthenticated &&
    !!currentUser &&
    !!channel &&
    currentUser.userId === channel.ownerId
  const canSubscribe = isAuthenticated && !isOwner

  const videos = videosQuery.data?.pages.flatMap((page) => page.videos) ?? []
  const playlists =
    playlistsQuery.data?.pages.flatMap((page) => page.items) ?? []
  const videoCount = videosQuery.isPending
    ? null
    : formatLoadedCount(videos.length, videosQuery.hasNextPage)
  const playlistsHaveNone = playlistsQuery.isSuccess && playlists.length === 0
  // With no playlist yet, the empty state below carries the same button — one is enough.
  const showsNewPlaylistInTabs =
    activeTab === 'playlists' && isOwner && playlists.length > 0
  const playlistCount = playlistsQuery.isPending
    ? null
    : formatLoadedCount(playlists.length, playlistsQuery.hasNextPage)

  if (isError) {
    return (
      <main className="page-container py-8">
        <QueryErrorState
          error={error}
          notFoundTitle="Channel not found"
          onRetry={() => refetch()}
          isRetrying={isRefetching}
        />
      </main>
    )
  }

  return (
    <main className="page-container py-6">
      {channelPending ? (
        <ChannelHeaderSkeleton />
      ) : (
        <ChannelHeader
          channel={channel}
          isOwner={isOwner}
          canSubscribe={canSubscribe}
        />
      )}

      {/* Fixed height, tabs on the bottom edge: the owner's "New playlist" appears on one tab only,
          and sizing the row by its content made the tabs jump when switching. */}
      <div className="mt-8 flex h-12 items-end justify-between border-b border-border">
        <nav aria-label="Channel sections" className="flex gap-6">
          <TabLink
            tab="videos"
            activeTab={activeTab}
            label="Videos"
            count={videoCount}
          />
          <TabLink
            tab="playlists"
            activeTab={activeTab}
            label="Playlists"
            count={playlistCount}
          />
        </nav>
        {showsNewPlaylistInTabs && (
          <Button
            variant="ghost"
            size="sm"
            className="mb-2"
            onClick={() => setNewPlaylistOpen(true)}
          >
            <Plus /> New playlist
          </Button>
        )}
      </div>

      <section className="mt-6">
        {activeTab === 'videos' && videosQuery.isError && (
          <ErrorState
            title="Could not load the videos"
            onRetry={() => videosQuery.refetch()}
            isRetrying={videosQuery.isRefetching}
          />
        )}

        {activeTab === 'videos' && !videosQuery.isError && (
          <>
            <VideoGrid
              videos={videos}
              isLoading={videosQuery.isPending}
              hideChannelInfo
              isOwner={isOwner}
              emptyState={
                <EmptyState
                  icon={Film}
                  title="No videos yet"
                  description={
                    isOwner
                      ? 'Your uploads show up here once they finish processing.'
                      : 'This channel has not uploaded anything.'
                  }
                  action={
                    isOwner && (
                      <Button asChild>
                        <Link to="/upload" className="no-underline">
                          <Upload /> Upload a video
                        </Link>
                      </Button>
                    )
                  }
                />
              }
            />
            {videosQuery.hasNextPage && (
              <div className="mt-8 flex justify-center">
                <Button
                  variant="outline"
                  onClick={() => videosQuery.fetchNextPage()}
                  disabled={videosQuery.isFetchingNextPage}
                >
                  {videosQuery.isFetchingNextPage ? 'Loading…' : 'Load more'}
                </Button>
              </div>
            )}
          </>
        )}

        {activeTab === 'playlists' && (
          <>
            {playlistsQuery.isPending && (
              <div className={CARD_GRID_CLASS}>
                {Array.from({ length: 4 }).map((_, i) => (
                  <Skeleton
                    // biome-ignore lint/suspicious/noArrayIndexKey: skeleton placeholders have no identity
                    key={i}
                    className="aspect-video w-full rounded-md"
                  />
                ))}
              </div>
            )}
            {playlistsQuery.isError && (
              <ErrorState
                title="Could not load the playlists"
                onRetry={() => playlistsQuery.refetch()}
                isRetrying={playlistsQuery.isRefetching}
              />
            )}
            {playlistsHaveNone && (
              <EmptyState
                icon={ListVideo}
                title="No playlists yet"
                description={
                  isOwner
                    ? 'Group videos from this channel into a playlist.'
                    : 'This channel has not made any playlists.'
                }
                action={
                  isOwner && (
                    <Button onClick={() => setNewPlaylistOpen(true)}>
                      <Plus /> New playlist
                    </Button>
                  )
                }
              />
            )}
            {playlists.length > 0 && (
              <div className={CARD_GRID_CLASS}>
                {playlists.map((playlist) => (
                  <PlaylistCard key={playlist.id} playlist={playlist} />
                ))}
              </div>
            )}
            {playlistsQuery.hasNextPage && (
              <div className="mt-8 flex justify-center">
                <Button
                  variant="outline"
                  onClick={() => playlistsQuery.fetchNextPage()}
                  disabled={playlistsQuery.isFetchingNextPage}
                >
                  {playlistsQuery.isFetchingNextPage ? 'Loading…' : 'Load more'}
                </Button>
              </div>
            )}
          </>
        )}
      </section>

      {channel && (
        <Dialog open={newPlaylistOpen} onOpenChange={setNewPlaylistOpen}>
          <DialogContent>
            <DialogHeader>
              <DialogTitle>New playlist</DialogTitle>
              <DialogDescription>
                A playlist of this channel’s videos, shown on its Playlists tab.
              </DialogDescription>
            </DialogHeader>
            <CreatePlaylistForm
              channelId={channel.channelId}
              onSuccess={() => setNewPlaylistOpen(false)}
            />
          </DialogContent>
        </Dialog>
      )}
    </main>
  )
}
