import { Link } from '@tanstack/react-router'
import { ImagePlus, Pencil } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { Avatar, AvatarFallback, AvatarImage } from '#/components/ui/avatar'
import { Badge } from '#/components/ui/badge'
import { Button } from '#/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from '#/components/ui/dialog'
import { cn } from '#/lib/utils'
import type { EnumValue } from '#/shared/api'
import { VideoVisibility } from '#/shared/types'
import { useUploadThumbnail } from '../hooks'
import { EditVideoForm } from './EditVideoForm'

export type VideoCardVideo = {
  videoId: string
  thumbnailUrls: string[]
  title: string
  description?: string | null
  channelName?: string
  ownerUsername: string
  channelHandle: string
  channelAvatarUrl: string | null
  tags: string[]
  viewCount: number
  likeCount: number
  dislikeCount?: number
  createdAt: string
  visibility?: EnumValue
  status?: EnumValue
}

type VideoCardProps = {
  video: VideoCardVideo
  hideChannelInfo?: boolean
  isOwner?: boolean
  // Spans two columns and two rows of the grid (from sm up) — the first card of a page.
  isFeatured?: boolean
}

const VISIBLE_TAG_LIMIT = 3
// Each extra frame stays up for the cycle and cross-fades in over the fade; the fade has to be
// shorter than the cycle, or a frame never settles before the next one starts.
const THUMBNAIL_CYCLE_MS = 1000
const THUMBNAIL_FADE_CLASS = 'duration-500'
const FIRST_FRAME_DELAY_MS = 150

export function formatCount(count: number): string {
  if (count >= 1_000_000) return `${(count / 1_000_000).toFixed(1)}M`
  if (count >= 1_000) return `${(count / 1_000).toFixed(1)}K`
  return String(count)
}

// "4h", "3d", "2w" — the card's metadata line is mono and has to stay short next to the channel.
export function formatCompactAge(isoDate: string, now = Date.now()): string {
  const minutes = Math.floor((now - new Date(isoDate).getTime()) / 60_000)
  if (minutes < 1) return 'now'
  if (minutes < 60) return `${minutes}m`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours}h`
  const days = Math.floor(hours / 24)
  if (days < 7) return `${days}d`
  if (days < 30) return `${Math.floor(days / 7)}w`
  if (days < 365) return `${Math.floor(days / 30)}mo`
  return `${Math.floor(days / 365)}y`
}

function visibilityBadgeVariant(
  visibilityId: number,
): 'default' | 'secondary' | 'outline' {
  if (visibilityId === VideoVisibility.Public) return 'default'
  if (visibilityId === VideoVisibility.Unlisted) return 'secondary'
  return 'outline'
}

// On hover the thumbnail cycles through the extra frames the worker generated.
function useThumbnailCycling(thumbnailUrls: string[]) {
  // The frame on show while hovered (0 is the main one); null when not hovered.
  const [hoveredIndex, setHoveredIndex] = useState<number | null>(null)
  const firstFrameTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(
    null,
  )
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null)
  const hasExtraThumbnails = thumbnailUrls.length > 1

  function clearTimers() {
    if (firstFrameTimeoutRef.current) clearTimeout(firstFrameTimeoutRef.current)
    if (intervalRef.current) clearInterval(intervalRef.current)
    firstFrameTimeoutRef.current = null
    intervalRef.current = null
  }

  // Index 0 first mounts the extra frames invisible; only then does frame 1 get to fade in. Going
  // straight to 1 would mount it already visible, and the first switch would be a hard cut.
  function startCycling() {
    if (!hasExtraThumbnails) return
    setHoveredIndex(0)
    firstFrameTimeoutRef.current = setTimeout(() => {
      let index = 1
      setHoveredIndex(index)
      intervalRef.current = setInterval(() => {
        index = (index % (thumbnailUrls.length - 1)) + 1
        setHoveredIndex(index)
      }, THUMBNAIL_CYCLE_MS)
    }, FIRST_FRAME_DELAY_MS)
  }

  function stopCycling() {
    clearTimers()
    setHoveredIndex(null)
  }

  // biome-ignore lint/correctness/useExhaustiveDependencies: unmount-only cleanup; clearTimers only touches refs
  useEffect(() => clearTimers, [])

  return { hoveredIndex, startCycling, stopCycling }
}

type ThumbnailStackProps = {
  thumbnailUrls: string[]
  hoveredIndex: number | null
}

// The main frame always sits at the bottom; the extra frames are stacked on top and cross-fade by
// opacity. A frame still loading is transparent, so the main one shows through instead of a blank
// flash. The extra frames only mount on the first hover — a page of cards does not download every
// preview frame up front.
function ThumbnailStack({ thumbnailUrls, hoveredIndex }: ThumbnailStackProps) {
  const [mainThumbnailUrl, ...extraThumbnailUrls] = thumbnailUrls
  const [hasBeenHovered, setHasBeenHovered] = useState(false)
  const isHovered = hoveredIndex !== null
  if (isHovered && !hasBeenHovered) setHasBeenHovered(true)

  if (!mainThumbnailUrl) {
    return (
      <div className="flex h-full w-full items-center justify-center">
        <span className="text-sm text-muted-foreground">No thumbnail</span>
      </div>
    )
  }

  return (
    <>
      <img
        src={mainThumbnailUrl}
        alt=""
        className="absolute inset-0 h-full w-full object-cover"
      />
      {hasBeenHovered &&
        extraThumbnailUrls.map((url, extraIndex) => {
          const isShown = hoveredIndex === extraIndex + 1
          return (
            <img
              key={url}
              src={url}
              alt=""
              className={cn(
                'absolute inset-0 h-full w-full object-cover transition-opacity ease-in-out motion-reduce:transition-none',
                THUMBNAIL_FADE_CLASS,
                isShown ? 'opacity-100' : 'opacity-0',
              )}
            />
          )
        })}
    </>
  )
}

// The title link stretches over the whole card (`after:inset-0`), so the card is one click target
// without wrapping it in an <a> — the channel link and the owner buttons sit above it (`z-10`).
// The old card nested the channel <a> inside the card's <a>: invalid HTML, and React warned.
export function VideoCard({
  video,
  hideChannelInfo = false,
  isOwner = false,
  isFeatured = false,
}: VideoCardProps) {
  const [editOpen, setEditOpen] = useState(false)
  const thumbnailInputRef = useRef<HTMLInputElement>(null)
  const uploadThumbnail = useUploadThumbnail(video.videoId)
  const { hoveredIndex, startCycling, stopCycling } = useThumbnailCycling(
    video.thumbnailUrls,
  )

  const channelName = video.channelName ?? video.channelHandle
  const channelInitial = channelName.charAt(0).toUpperCase()
  const visibleTags = video.tags.slice(0, VISIBLE_TAG_LIMIT)
  const isNotPublic =
    video.visibility !== undefined &&
    video.visibility.id !== VideoVisibility.Public
  const isNotReady =
    video.status !== undefined && video.status.value !== 'Ready'
  const publishedOn = new Date(video.createdAt).toLocaleDateString()

  function handleThumbnailFileChange(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    if (!file) return
    uploadThumbnail.mutate(file)
    e.target.value = ''
  }

  return (
    <>
      <article
        className={cn(
          'group relative flex flex-col rounded-lg transition-colors sm:-m-2 sm:p-2 hover:bg-card hover:shadow-sm hover:ring-1 hover:ring-border has-[a:focus-visible]:ring-2 has-[a:focus-visible]:ring-ring',
          isFeatured && 'sm:col-span-2 sm:row-span-2',
        )}
        onMouseEnter={startCycling}
        onMouseLeave={stopCycling}
      >
        <div className="relative aspect-video w-full overflow-hidden rounded-md bg-muted">
          <ThumbnailStack
            thumbnailUrls={video.thumbnailUrls}
            hoveredIndex={hoveredIndex}
          />

          {isOwner && isNotReady && video.status && (
            <div className="absolute inset-0 flex items-center justify-center bg-black/50">
              <span className="text-xs font-medium text-white">
                {video.status.value}
              </span>
            </div>
          )}

          {isOwner && isNotPublic && video.visibility && (
            <div className="absolute top-2 left-2">
              <Badge
                variant={visibilityBadgeVariant(video.visibility.id)}
                className="text-xs"
              >
                {video.visibility.value}
              </Badge>
            </div>
          )}

          {visibleTags.length > 0 && (
            <ul
              aria-label="Tags"
              className="absolute bottom-2 left-2 flex max-w-[70%] gap-1 overflow-hidden opacity-0 transition-opacity group-hover:opacity-100"
            >
              {visibleTags.map((tag) => (
                <li
                  key={tag}
                  className="shrink-0 rounded bg-black/70 px-1.5 py-0.5 font-mono text-[10px] text-white"
                >
                  #{tag}
                </li>
              ))}
            </ul>
          )}

          {isOwner && (
            <Button
              variant="secondary"
              size="icon"
              className="absolute top-2 right-2 z-10 h-7 w-7 opacity-50 transition-opacity group-hover:opacity-100"
              onClick={() => setEditOpen(true)}
              aria-label="Edit video"
            >
              <Pencil className="h-3.5 w-3.5" />
            </Button>
          )}

          {isOwner && (
            <Button
              variant="secondary"
              size="icon"
              className="absolute right-2 bottom-2 z-10 h-7 w-7 opacity-50 transition-opacity group-hover:opacity-100"
              onClick={() => thumbnailInputRef.current?.click()}
              disabled={uploadThumbnail.isPending}
              aria-label="Upload thumbnail"
            >
              <ImagePlus className="h-3.5 w-3.5" />
            </Button>
          )}
        </div>

        <div className="mt-2.5 px-0.5">
          <h3
            className={cn(
              'line-clamp-2 leading-snug font-bold text-foreground',
              isFeatured ? 'text-base sm:text-xl' : 'text-sm',
            )}
          >
            <Link
              to="/watch/$videoId"
              params={{ videoId: video.videoId }}
              className="text-foreground no-underline outline-none after:absolute after:inset-0 after:rounded-lg after:content-['']"
            >
              {video.title}
            </Link>
          </h3>

          <div className="mt-1.5 flex items-center gap-2 text-muted-foreground">
            {!hideChannelInfo && (
              <Link
                to="/$username/$channel"
                params={{
                  username: video.ownerUsername,
                  channel: video.channelHandle,
                }}
                className="relative z-10 flex min-w-0 items-center gap-2 text-xs font-semibold text-muted-foreground no-underline transition-colors hover:text-foreground"
              >
                <Avatar className="size-5">
                  <AvatarImage src={video.channelAvatarUrl ?? undefined} />
                  <AvatarFallback className="text-[10px]">
                    {channelInitial}
                  </AvatarFallback>
                </Avatar>
                <span className="truncate">{channelName}</span>
              </Link>
            )}
            <span className="ml-auto shrink-0 font-mono text-xs">
              {formatCount(video.viewCount)} views ·{' '}
              <time dateTime={video.createdAt} title={publishedOn}>
                {formatCompactAge(video.createdAt)}
              </time>
            </span>
          </div>
        </div>
      </article>

      {isOwner && (
        <input
          ref={thumbnailInputRef}
          type="file"
          accept="image/*"
          className="hidden"
          onChange={handleThumbnailFileChange}
        />
      )}

      {isOwner && video.visibility && (
        <Dialog open={editOpen} onOpenChange={setEditOpen}>
          <DialogContent>
            <DialogHeader>
              <DialogTitle>Edit video</DialogTitle>
            </DialogHeader>
            <EditVideoForm
              video={video as Parameters<typeof EditVideoForm>[0]['video']}
              onSuccess={() => setEditOpen(false)}
            />
          </DialogContent>
        </Dialog>
      )}
    </>
  )
}
