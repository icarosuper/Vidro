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
const THUMBNAIL_CYCLE_MS = 800

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
  const [hoveredIndex, setHoveredIndex] = useState<number | null>(null)
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null)
  const hasExtraThumbnails = thumbnailUrls.length > 1

  function startCycling() {
    if (!hasExtraThumbnails) return
    let index = 1
    setHoveredIndex(index)
    intervalRef.current = setInterval(() => {
      index = (index % (thumbnailUrls.length - 1)) + 1
      setHoveredIndex(index)
    }, THUMBNAIL_CYCLE_MS)
  }

  function stopCycling() {
    if (intervalRef.current) {
      clearInterval(intervalRef.current)
      intervalRef.current = null
    }
    setHoveredIndex(null)
  }

  useEffect(
    () => () => {
      if (intervalRef.current) clearInterval(intervalRef.current)
    },
    [],
  )

  const shownIndex = hoveredIndex ?? 0
  const thumbnailUrl = thumbnailUrls[shownIndex] ?? null
  return { thumbnailUrl, startCycling, stopCycling }
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
  const { thumbnailUrl, startCycling, stopCycling } = useThumbnailCycling(
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
          {thumbnailUrl ? (
            <img
              src={thumbnailUrl}
              alt=""
              className="h-full w-full object-cover"
            />
          ) : (
            <div className="flex h-full w-full items-center justify-center">
              <span className="text-sm text-muted-foreground">
                No thumbnail
              </span>
            </div>
          )}

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
