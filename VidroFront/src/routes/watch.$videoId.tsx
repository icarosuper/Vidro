import { Link, createFileRoute } from '@tanstack/react-router'
import { ThumbsDown, ThumbsUp } from 'lucide-react'
import { useEffect } from 'react'
import { Avatar, AvatarFallback, AvatarImage } from '#/components/ui/avatar'
import { Badge } from '#/components/ui/badge'
import { Button } from '#/components/ui/button'
import { Separator } from '#/components/ui/separator'
import { useIsAuthenticated } from '#/features/auth/hooks'
import { SubscribeButton } from '#/features/channels/components/SubscribeButton'
import { CommentList } from '#/features/comments/components/CommentList'
import { useCurrentUser } from '#/features/users/hooks'
import { getVideo } from '#/features/videos/api'
import { fetchVideoSsr } from '#/features/videos/server'
import { VideoPlayer } from '#/features/videos/components/VideoPlayer'
import { useReactToVideo, useRegisterView, useRemoveReaction, useVideo, videoKeys } from '#/features/videos/hooks'
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
  },
  component: WatchPage,
})

function formatCount(count: number): string {
  if (count >= 1_000_000) return `${(count / 1_000_000).toFixed(1)}M`
  if (count >= 1_000) return `${(count / 1_000).toFixed(1)}K`
  return String(count)
}

function formatDate(isoDate: string): string {
  return new Date(isoDate).toLocaleDateString('en-US', {
    year: 'numeric',
    month: 'long',
    day: 'numeric',
  })
}

function WatchPage() {
  const { videoId } = Route.useParams()
  const { data: video, isPending, isError, error } = useVideo(videoId)
  const isAuthenticated = useIsAuthenticated()
  const { data: currentUser } = useCurrentUser()
  const registerView = useRegisterView(videoId)
  const reactToVideo = useReactToVideo(videoId)
  const removeReaction = useRemoveReaction(videoId)

  useEffect(() => {
    const videoIsReady = video !== undefined
    if (videoIsReady) {
      registerView.mutate()
    }
  // Only fire once when video loads — intentionally omitting registerView from deps
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [video?.videoId])

  if (isPending) {
    return (
      <main className="page-container py-8">
        <p className="text-muted-foreground">Loading…</p>
      </main>
    )
  }

  if (isError) {
    return (
      <main className="page-container py-8">
        <p className="text-destructive">{error.message}</p>
      </main>
    )
  }

  const thumbnailUrl = video.thumbnailUrls[0] ?? undefined
  const channelInitial = video.channelName.charAt(0).toUpperCase()
  const isMutating = reactToVideo.isPending || removeReaction.isPending
  const isOwnChannel = isAuthenticated && !!currentUser && currentUser.username === video.ownerUsername
  const canSubscribe = isAuthenticated && !isOwnChannel

  function handleLike() {
    if (!isAuthenticated) return

    const isAlreadyLiked = video.userReaction?.id === ReactionType.Like

    if (isAlreadyLiked) {
      removeReaction.mutate()
    } else {
      reactToVideo.mutate(ReactionType.Like)
    }
  }

  function handleDislike() {
    if (!isAuthenticated) return

    const isAlreadyDisliked = video.userReaction?.id === ReactionType.Dislike

    if (isAlreadyDisliked) {
      removeReaction.mutate()
    } else {
      reactToVideo.mutate(ReactionType.Dislike)
    }
  }

  return (
    <main className="page-container py-8">
      <div className="flex flex-col gap-6 lg:flex-row">
        <div className="flex-1 min-w-0">
          {/* Player */}
          <div className="aspect-video w-full">
            {video.videoUrl
              ? <VideoPlayer src={video.videoUrl} poster={thumbnailUrl} />
              : (
                <div className="flex h-full w-full items-center justify-center rounded-lg bg-muted">
                  <p className="text-muted-foreground">
                    {video.status.value === 'Processing'
                      ? 'Video is being processed…'
                      : 'Video unavailable'}
                  </p>
                </div>
              )}
          </div>

          {/* Title & metadata */}
          <div className="mt-4">
            <h1 className="text-xl font-bold leading-tight">{video.title}</h1>

            <div className="mt-3 flex flex-wrap items-center justify-between gap-4">
              {/* Channel info + subscribe */}
              <div className="flex items-center gap-3">
                <Link
                  to="/$username/$channel"
                  params={{ username: video.ownerUsername, channel: video.channelHandle }}
                  className="no-underline flex items-center gap-3 group"
                >
                  <Avatar className="h-9 w-9">
                    <AvatarImage
                      src={video.channelAvatarUrl ?? undefined}
                      alt={video.channelName}
                    />
                    <AvatarFallback>{channelInitial}</AvatarFallback>
                  </Avatar>
                  <div>
                    <p className="text-sm font-medium group-hover:underline">{video.channelName}</p>
                    <p className="text-xs text-muted-foreground">
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

              {/* Views + Reaction buttons */}
              <div className="flex items-center gap-3">
                <span className="text-sm text-muted-foreground">
                  {formatCount(video.viewCount)} views
                </span>
                <Button
                  variant={video.userReaction?.id === ReactionType.Like ? 'default' : 'outline'}
                  size="sm"
                  onClick={handleLike}
                  disabled={isMutating || !isAuthenticated || isOwnChannel}
                  title={isAuthenticated ? undefined : 'Sign in to react'}
                >
                  <ThumbsUp className="mr-1.5 h-4 w-4" />
                  {formatCount(video.likeCount)}
                </Button>
                <Button
                  variant={video.userReaction?.id === ReactionType.Dislike ? 'default' : 'outline'}
                  size="sm"
                  onClick={handleDislike}
                  disabled={isMutating || !isAuthenticated || isOwnChannel}
                  title={isAuthenticated ? undefined : 'Sign in to react'}
                >
                  <ThumbsDown className="mr-1.5 h-4 w-4" />
                  {formatCount(video.dislikeCount)}
                </Button>
              </div>
            </div>

            <Separator className="my-4" />

            {/* Date + Description */}
            <p className="text-xs text-muted-foreground mb-2">{formatDate(video.createdAt)}</p>
            {video.description && (
              <p className="whitespace-pre-wrap text-sm text-muted-foreground">
                {video.description}
              </p>
            )}

            {/* Tags */}
            {video.tags.length > 0 && (
              <div className="mt-3 flex flex-wrap gap-2">
                {video.tags.map((tag) => (
                  <Badge key={tag} variant="secondary">
                    {tag}
                  </Badge>
                ))}
              </div>
            )}

            <Separator className="my-6" />

            <div className="max-w-2xl">
              <CommentList videoId={video.videoId} currentUserId={currentUser?.id} />
            </div>
          </div>
        </div>
      </div>
    </main>
  )
}
