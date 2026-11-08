import { createFileRoute, Link } from '@tanstack/react-router'
import { ThumbsDown, ThumbsUp } from 'lucide-react'
import { useEffect } from 'react'
import { Avatar, AvatarFallback, AvatarImage } from '#/components/ui/avatar'
import { Skeleton } from '#/components/ui/skeleton'
import { useIsAuthenticated } from '#/features/auth/hooks'
import { SubscribeButton } from '#/features/channels/components/SubscribeButton'
import { CommentList } from '#/features/comments/components/CommentList'
import { ExpandableText } from '#/features/comments/components/ExpandableText'
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
import { seo } from '#/shared/lib/seo'
import { ReactionType } from '#/shared/types'

export const Route = createFileRoute('/watch/$videoId')({
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

function WatchPage() {
  const { videoId } = Route.useParams()
  const { data: video, isPending, isError, error } = useVideo(videoId)
  const isAuthenticated = useIsAuthenticated()
  const { data: currentUser } = useCurrentUser()
  const registerView = useRegisterView(videoId)
  const reactToVideo = useReactToVideo(videoId)
  const removeReaction = useRemoveReaction(videoId)
  const upNext = useUpNext(video)

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
        <p className="text-destructive">{error.message}</p>
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
    <main className="page-container py-6">
      <div className="grid gap-8 xl:grid-cols-[minmax(0,1fr)_360px] xl:grid-rows-[auto_1fr]">
        <div className="min-w-0">
          <div className="aspect-video w-full overflow-hidden rounded-lg bg-black">
            {video.videoUrl ? (
              <VideoPlayer src={video.videoUrl} poster={thumbnailUrl} />
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

          <h1 className="mt-5 text-2xl leading-tight font-extrabold tracking-tight">
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

            <ReactionPill
              likeCount={video.likeCount}
              dislikeCount={video.dislikeCount}
              userReactionId={userReactionId}
              disabled={isMutating || !isAuthenticated || isOwnChannel}
              disabledReason={reactionDisabledReason}
              onLike={handleLike}
              onDislike={handleDislike}
            />
          </div>

          <section
            aria-label="Description"
            className="mt-5 rounded-lg bg-muted p-4"
          >
            <p className="font-mono text-xs font-medium text-foreground">
              {formatCount(video.viewCount)} views ·{' '}
              {formatDate(video.createdAt)}
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
            thread; from xl it moves to the right column, spanning both rows. */}
        <aside
          aria-labelledby="up-next-title"
          className="min-w-0 xl:col-start-2 xl:row-span-2 xl:row-start-1"
        >
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

        <div className="min-w-0 max-w-3xl">
          <CommentList
            videoId={video.videoId}
            currentUserId={currentUser?.userId}
          />
        </div>
      </div>
    </main>
  )
}
