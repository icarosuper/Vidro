import { createFileRoute, Link } from '@tanstack/react-router'
import {
  RectangleHorizontal,
  RectangleVertical,
  ThumbsDown,
  ThumbsUp,
} from 'lucide-react'
import { useEffect } from 'react'
import { QueryErrorState } from '#/components/StatePanel'
import { Avatar, AvatarFallback, AvatarImage } from '#/components/ui/avatar'
import { Button } from '#/components/ui/button'
import { Skeleton } from '#/components/ui/skeleton'
import { useIsAuthenticated } from '#/features/auth/hooks'
import { SubscribeButton } from '#/features/channels/components/SubscribeButton'
import { CommentList } from '#/features/comments/components/CommentList'
import { ExpandableText } from '#/features/comments/components/ExpandableText'
import {
  findNextInPlaylist,
  PlaylistQueue,
} from '#/features/playlists/components/PlaylistQueue'
import { SaveToPlaylistButton } from '#/features/playlists/components/SaveToPlaylistButton'
import { usePlaylist } from '#/features/playlists/hooks'
import { useCurrentUser } from '#/features/users/hooks'
import { getVideo } from '#/features/videos/api'
import {
  pickUpNextVideos,
  UpNextList,
} from '#/features/videos/components/UpNextList'
import { formatCount } from '#/features/videos/components/VideoCard'
import { VideoPlayer } from '#/features/videos/components/VideoPlayer'
import {
  useChannelVideos,
  useReactToVideo,
  useRegisterView,
  useRemoveReaction,
  useTrending,
  useVideo,
  videoKeys,
} from '#/features/videos/hooks'
import { fetchVideoSsr } from '#/features/videos/server'
import type { Video } from '#/features/videos/types'
import { cn } from '#/lib/utils'
import { isTypingInField } from '#/shared/lib/keyboard'
import { useLayoutPreference } from '#/shared/lib/layout-preferences'
import { seo } from '#/shared/lib/seo'
import { ReactionType } from '#/shared/types'

// `?list=<playlistId>`: the video is being played from that playlist — the right column shows its
// queue and the next video starts when this one ends. Absent, the page is the plain watch page.
type WatchSearch = { list?: string }

export const Route = createFileRoute('/watch/$videoId')({
  validateSearch: (search: Record<string, unknown>): WatchSearch => ({
    list: typeof search.list === 'string' ? search.list : undefined,
  }),
  loader: async ({ params, context: { queryClient, accessToken } }) => {
    await queryClient.prefetchQuery({
      queryKey: videoKeys.detail(params.videoId),
      queryFn: () =>
        typeof window === 'undefined'
          ? fetchVideoSsr(params.videoId, accessToken)
          : getVideo(params.videoId),
    })

    // Read from the cache instead of returning the fetch: prefetchQuery swallows the error, so a
    // video that failed to load leaves this undefined and the page keeps rendering its own error
    // state. Returning the query itself would turn a broken meta tag into a broken page.
    const video = queryClient.getQueryData<Video>(
      videoKeys.detail(params.videoId),
    )
    return { video }
  },
  // The one page people paste into a chat — without this, every share previews as "Vidro".
  head: ({ loaderData }) => {
    const video = loaderData?.video
    if (!video) return {}

    return {
      meta: seo({
        title: video.title,
        description:
          video.description ??
          `${video.title}, by @${video.ownerUsername}/${video.channelHandle} on Vidro.`,
        // Presigned MinIO URL: it expires, so a card cached for long enough loses its image.
        image: video.thumbnailUrls[0],
        type: 'video.other',
      }),
    }
  },
  component: WatchPage,
})

function formatDate(isoDate: string): string {
  return new Date(isoDate).toLocaleDateString('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  })
}

function WatchPageSkeleton() {
  return (
    <main className="page-container py-6">
      <Skeleton className="aspect-video w-full rounded-lg xl:w-[calc(100%-392px)]" />
      <Skeleton className="mt-5 h-7 w-2/3" />
      <div className="mt-4 flex items-center gap-3">
        <Skeleton className="size-10 rounded-full" />
        <Skeleton className="h-4 w-40" />
      </div>
    </main>
  )
}

// The channel's other videos, topped up with trending, for the "Up next" column.
// The playlist in `?list=`, but only when the video on screen is in it: a stale or hand-edited
// link falls back to the plain "Up next" instead of a queue that does not hold this video.
function usePlayingPlaylist(playlistId: string | undefined, videoId: string) {
  const { data: playlist } = usePlaylist(playlistId)
  const holdsThisVideo =
    !!playlist && playlist.items.some((item) => item.videoId === videoId)
  return { playlist: holdsThisVideo ? playlist : undefined }
}

function useUpNext(video: Video | undefined) {
  const channelVideosQuery = useChannelVideos(
    video?.ownerUsername,
    video?.channelHandle,
  )
  const trendingQuery = useTrending()
  if (!video) return { upNextVideos: [], isLoading: true }

  // The channel listing has no channel name — it is the channel on screen, so borrow it.
  const channelVideos = (channelVideosQuery.data?.pages[0]?.videos ?? []).map(
    (channelVideo) => ({ ...channelVideo, channelName: video.channelName }),
  )
  const trendingVideos = trendingQuery.data?.videos ?? []
  const upNextVideos = pickUpNextVideos(
    video.videoId,
    channelVideos,
    trendingVideos,
  )
  const isLoading = channelVideosQuery.isPending || trendingQuery.isPending
  return { upNextVideos, isLoading }
}

type ReactionPillProps = {
  likeCount: number
  dislikeCount: number
  userReactionId: number | undefined
  disabled: boolean
  disabledReason: string | undefined
  onLike: () => void
  onDislike: () => void
}

// Like and dislike as one segmented control; the chosen side is tinted with the accent.
function ReactionPill({
  likeCount,
  dislikeCount,
  userReactionId,
  disabled,
  disabledReason,
  onLike,
  onDislike,
}: ReactionPillProps) {
  const isLiked = userReactionId === ReactionType.Like
  const isDisliked = userReactionId === ReactionType.Dislike
  const sideClass =
    'flex items-center gap-2 px-4 py-2 text-sm font-semibold transition-colors hover:bg-accent disabled:pointer-events-none disabled:opacity-60'
  const chosenClass = 'bg-accent text-accent-foreground'

  return (
    <div className="flex items-center overflow-hidden rounded-full border border-border">
      <button
        type="button"
        onClick={onLike}
        disabled={disabled}
        title={disabledReason}
        aria-pressed={isLiked}
        className={cn(sideClass, isLiked && chosenClass)}
      >
        <ThumbsUp className={cn('size-4', isLiked && 'fill-current')} />
        <span className="sr-only">Like</span>
        <span className="font-mono">{formatCount(likeCount)}</span>
      </button>
      <span aria-hidden className="h-5 w-px bg-border" />
      <button
        type="button"
        onClick={onDislike}
        disabled={disabled}
        title={disabledReason}
        aria-pressed={isDisliked}
        className={cn(sideClass, isDisliked && chosenClass)}
      >
        <ThumbsDown className={cn('size-4', isDisliked && 'fill-current')} />
        <span className="sr-only">Dislike</span>
        <span className="font-mono">{formatCount(dislikeCount)}</span>
      </button>
    </div>
  )
}

// "t" toggles it too, like YouTube. From xl only: below it the page is a single column anyway.
function TheaterModeButton() {
  const [isTheater, toggleTheater] = useLayoutPreference('theater')

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent) {
      const isTheaterShortcut =
        event.key === 't' && !event.ctrlKey && !event.metaKey && !event.altKey
      if (!isTheaterShortcut || isTypingInField(event.target)) return
      toggleTheater()
    }
    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [toggleTheater])

  const label = isTheater ? 'Default view (t)' : 'Theater mode (t)'
  const Icon = isTheater ? RectangleVertical : RectangleHorizontal

  return (
    <Button
      variant="outline"
      size="icon"
      className="hidden rounded-full xl:inline-flex"
      onClick={toggleTheater}
      aria-label="Theater mode"
      aria-pressed={isTheater}
      title={label}
    >
      <Icon className="size-4" />
    </Button>
  )
}

function WatchPage() {
  const { videoId } = Route.useParams()
  const {
    data: video,
    isPending,
    isError,
    error,
    refetch,
    isRefetching,
  } = useVideo(videoId)
  const isAuthenticated = useIsAuthenticated()
  const { data: currentUser } = useCurrentUser()
  const registerView = useRegisterView(videoId)
  const reactToVideo = useReactToVideo(videoId)
  const removeReaction = useRemoveReaction(videoId)
  const upNext = useUpNext(video)
  const { list: playlistId } = Route.useSearch()
  const queue = usePlayingPlaylist(playlistId, videoId)
  const navigate = Route.useNavigate()

  function playNextInPlaylist() {
    const next = queue.playlist && findNextInPlaylist(queue.playlist, videoId)
    if (!next) return
    navigate({
      to: '/watch/$videoId',
      params: { videoId: next.videoId },
      search: { list: playlistId },
    })
  }

  // One view per loaded video. The id — not `video` — is the dependency: depending on the
  // object re-registers on every refetch, and navigating between videos reuses this route
  // instead of remounting it, so the id changing is what has to re-fire the effect.
  // `mutate` is a stable reference from useMutation.
  const loadedVideoId = video?.videoId
  const registerViewOnce = registerView.mutate

  useEffect(() => {
    if (loadedVideoId === undefined) return
    registerViewOnce()
  }, [loadedVideoId, registerViewOnce])

  if (isPending) return <WatchPageSkeleton />

  if (isError) {
    return (
      <main className="page-container py-8">
        <QueryErrorState
          error={error}
          notFoundTitle="Video not found"
          onRetry={() => refetch()}
          isRetrying={isRefetching}
        />
      </main>
    )
  }

  const thumbnailUrl = video.thumbnailUrls[0] ?? undefined
  const userReactionId = video.userReaction?.id
  const channelInitial = video.channelName.charAt(0).toUpperCase()
  const isMutating = reactToVideo.isPending || removeReaction.isPending
  const isOwnChannel =
    isAuthenticated &&
    !!currentUser &&
    currentUser.username === video.ownerUsername
  const canSubscribe = isAuthenticated && !isOwnChannel
  const reactionDisabledReason = isAuthenticated
    ? undefined
    : 'Sign in to react'

  function handleLike() {
    if (!isAuthenticated) return

    const isAlreadyLiked = userReactionId === ReactionType.Like

    if (isAlreadyLiked) {
      removeReaction.mutate()
    } else {
      reactToVideo.mutate(ReactionType.Like)
    }
  }

  function handleDislike() {
    if (!isAuthenticated) return

    const isAlreadyDisliked = userReactionId === ReactionType.Dislike

    if (isAlreadyDisliked) {
      removeReaction.mutate()
    } else {
      reactToVideo.mutate(ReactionType.Dislike)
    }
  }

  return (
    // The areas live in styles.css (`.watch-layout`): theater mode only moves them, by CSS.
    <main className="watch-layout py-6 xl:theater:pt-0">
      <div className="watch-player">
        {video.videoUrl ? (
          <VideoPlayer
            src={video.videoUrl}
            poster={thumbnailUrl}
            onEnded={playNextInPlaylist}
            autoPlay={!!queue.playlist}
          />
        ) : (
          <div className="flex h-full w-full items-center justify-center bg-muted">
            <p className="text-muted-foreground">
              {video.status.value === 'Processing'
                ? 'Video is being processed…'
                : 'Video unavailable'}
            </p>
          </div>
        )}
      </div>

      <div className="watch-info">
        <h1 className="text-2xl leading-tight font-extrabold tracking-tight">
          {video.title}
        </h1>

        <div className="mt-4 flex flex-wrap items-center justify-between gap-4">
          <div className="flex min-w-0 items-center gap-3">
            <Link
              to="/$username/$channel"
              params={{
                username: video.ownerUsername,
                channel: video.channelHandle,
              }}
              className="group flex min-w-0 items-center gap-3 no-underline"
            >
              <Avatar className="size-10">
                <AvatarImage
                  src={video.channelAvatarUrl ?? undefined}
                  alt={video.channelName}
                />
                <AvatarFallback>{channelInitial}</AvatarFallback>
              </Avatar>
              <div className="min-w-0">
                <p className="truncate font-bold text-foreground group-hover:text-primary">
                  {video.channelName}
                </p>
                <p className="truncate font-mono text-xs text-muted-foreground">
                  @{video.ownerUsername}/{video.channelHandle}
                </p>
              </div>
            </Link>
            {canSubscribe && (
              <SubscribeButton
                username={video.ownerUsername}
                handle={video.channelHandle}
                initialIsFollowing={video.isFollowingChannel}
              />
            )}
          </div>

          <div className="flex items-center gap-2">
            <ReactionPill
              likeCount={video.likeCount}
              dislikeCount={video.dislikeCount}
              userReactionId={userReactionId}
              disabled={isMutating || !isAuthenticated || isOwnChannel}
              disabledReason={reactionDisabledReason}
              onLike={handleLike}
              onDislike={handleDislike}
            />
            <SaveToPlaylistButton
              videoId={video.videoId}
              ownChannelHandle={isOwnChannel ? video.channelHandle : undefined}
            />
            <TheaterModeButton />
          </div>
        </div>

        <section
          aria-label="Description"
          className="mt-5 rounded-lg bg-muted p-4"
        >
          <p className="font-mono text-xs font-medium text-foreground">
            {formatCount(video.viewCount)} views · {formatDate(video.createdAt)}
          </p>
          {video.description && (
            <ExpandableText
              text={video.description}
              className="mt-2 text-sm whitespace-pre-wrap text-foreground/90"
            />
          )}
          {video.tags.length > 0 && (
            <ul
              aria-label="Tags"
              className="mt-3 flex flex-wrap gap-x-3 gap-y-1"
            >
              {video.tags.map((tag) => (
                <li key={tag} className="font-mono text-xs text-primary">
                  #{tag}
                </li>
              ))}
            </ul>
          )}
        </section>
      </div>

      {/* After the description in the DOM, so on a phone "Up next" comes before a long comment
          thread; from xl it moves to the right column. */}
      {queue.playlist ? (
        <aside className="watch-up-next">
          <PlaylistQueue playlist={queue.playlist} currentVideoId={videoId} />
        </aside>
      ) : (
        <aside aria-labelledby="up-next-title" className="watch-up-next">
          <h2
            id="up-next-title"
            className="mb-3 font-mono text-xs tracking-widest text-muted-foreground uppercase"
          >
            Up next
          </h2>
          <UpNextList
            videos={upNext.upNextVideos}
            isLoading={upNext.isLoading}
          />
        </aside>
      )}

      <div className="watch-comments max-w-3xl">
        <CommentList
          videoId={video.videoId}
          currentUserId={currentUser?.userId}
        />
      </div>
    </main>
  )
}
