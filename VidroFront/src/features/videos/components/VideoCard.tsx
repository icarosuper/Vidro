import { Link } from '@tanstack/react-router'
import { Eye, ImagePlus, Pencil, ThumbsDown, ThumbsUp } from 'lucide-react'
import { useEffect, useRef, useState } from 'react'
import { Avatar, AvatarFallback, AvatarImage } from '#/components/ui/avatar'
import { Badge } from '#/components/ui/badge'
import { Button } from '#/components/ui/button'
import { Card, CardContent } from '#/components/ui/card'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from '#/components/ui/dialog'
import { TimeAgo } from '#/components/TimeAgo'
import { VideoVisibility } from '#/shared/types'
import type { EnumValue } from '#/shared/types'
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
}

function formatCount(count: number): string {
  if (count >= 1_000_000) return `${(count / 1_000_000).toFixed(1)}M`
  if (count >= 1_000) return `${(count / 1_000).toFixed(1)}K`
  return String(count)
}


function visibilityBadgeVariant(visibilityId: number): 'default' | 'secondary' | 'outline' {
  if (visibilityId === VideoVisibility.Public) return 'default'
  if (visibilityId === VideoVisibility.Unlisted) return 'secondary'
  return 'outline'
}

export function VideoCard({ video, hideChannelInfo = false, isOwner = false }: VideoCardProps) {
  const [editOpen, setEditOpen] = useState(false)
  const [hoveredIndex, setHoveredIndex] = useState<number | null>(null)
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null)
  const thumbnailInputRef = useRef<HTMLInputElement>(null)
  const hasExtraThumbnails = video.thumbnailUrls.length > 1
  const uploadThumbnail = useUploadThumbnail(video.videoId)

  function startCycling() {
    if (!hasExtraThumbnails) return
    let index = 1
    setHoveredIndex(index)
    intervalRef.current = setInterval(() => {
      index = (index % (video.thumbnailUrls.length - 1)) + 1
      setHoveredIndex(index)
    }, 800)
  }

  function stopCycling() {
    if (intervalRef.current) {
      clearInterval(intervalRef.current)
      intervalRef.current = null
    }
    setHoveredIndex(null)
  }

  useEffect(() => () => { if (intervalRef.current) clearInterval(intervalRef.current) }, [])

  const thumbnailUrl = hoveredIndex !== null ? (video.thumbnailUrls[hoveredIndex] ?? null) : (video.thumbnailUrls[0] ?? null)
  const channelInitial = (video.channelName ?? video.channelHandle).charAt(0).toUpperCase()
  const viewCountFormatted = formatCount(video.viewCount)
  const likeCountFormatted = formatCount(video.likeCount)
  const dislikeCountFormatted = video.dislikeCount !== undefined ? formatCount(video.dislikeCount) : null
  const isNotPublic = video.visibility !== undefined && video.visibility.id !== VideoVisibility.Public
  const isNotReady = video.status !== undefined && video.status.value !== 'Ready'

  function handleEditClick(e: React.MouseEvent) {
    e.preventDefault()
    e.stopPropagation()
    setEditOpen(true)
  }

  function handleThumbnailClick(e: React.MouseEvent) {
    e.preventDefault()
    e.stopPropagation()
    thumbnailInputRef.current?.click()
  }

  function handleThumbnailFileChange(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    if (!file) return
    uploadThumbnail.mutate(file)
    e.target.value = ''
  }

  return (
    <>
      <Link to="/watch/$videoId" params={{ videoId: video.videoId }} className="no-underline">
        <Card className="overflow-hidden transition-shadow hover:shadow-md border-0 shadow-none bg-transparent rounded-xl p-3 gap-0">
          <div
            className="group relative aspect-video w-full bg-muted rounded-xl overflow-hidden"
            onMouseEnter={startCycling}
            onMouseLeave={stopCycling}
          >
            {thumbnailUrl
              ? (
                <img
                  src={thumbnailUrl}
                  alt={video.title}
                  className="h-full w-full object-cover"
                />
              )
              : (
                <div className="flex h-full w-full items-center justify-center bg-muted">
                  <span className="text-muted-foreground text-sm">No thumbnail</span>
                </div>
              )}

            {isOwner && isNotReady && video.status && (
              <div className="absolute inset-0 flex items-center justify-center bg-black/50">
                <span className="text-xs font-medium text-white">{video.status.value}</span>
              </div>
            )}

            {isOwner && isNotPublic && video.visibility && (
              <div className="absolute top-2 left-2">
                <Badge variant={visibilityBadgeVariant(video.visibility.id)} className="text-xs">
                  {video.visibility.value}
                </Badge>
              </div>
            )}

            {isOwner && (
              <Button
                variant="secondary"
                size="icon"
                className="absolute top-2 right-2 h-7 w-7 opacity-50 group-hover:opacity-100 transition-opacity"
                onClick={handleEditClick}
                aria-label="Edit video"
              >
                <Pencil className="h-3.5 w-3.5" />
              </Button>
            )}

            {isOwner && (
              <Button
                variant="secondary"
                size="icon"
                className="absolute bottom-2 right-2 h-7 w-7 opacity-50 group-hover:opacity-100 transition-opacity"
                onClick={handleThumbnailClick}
                disabled={uploadThumbnail.isPending}
                aria-label="Upload thumbnail"
              >
                <ImagePlus className="h-3.5 w-3.5" />
              </Button>
            )}
          </div>

          <CardContent className="px-3 pt-2 pb-0">
            {hideChannelInfo
              ? (
                <div className="space-y-1.5">
                  <div className="flex items-start justify-between gap-2">
                    <h3 className="line-clamp-2 text-sm font-bold leading-tight text-foreground flex-1">
                      {video.title}
                    </h3>
                  </div>

                  <div className="flex items-center gap-2 text-xs text-muted-foreground">
                    <span className="flex items-center gap-1">
                      <Eye className="h-3 w-3" />
                      {viewCountFormatted}
                    </span>
                    <span className="flex items-center gap-1">
                      <ThumbsUp className="h-3 w-3" />
                      {likeCountFormatted}
                    </span>
                    {dislikeCountFormatted !== null && (
                      <span className="flex items-center gap-1">
                        <ThumbsDown className="h-3 w-3" />
                        {dislikeCountFormatted}
                      </span>
                    )}
                    <TimeAgo isoDate={video.createdAt} className="ml-auto" />
                  </div>

                  {video.tags.length > 0 && (
                    <div className="flex min-w-0 gap-1 overflow-hidden">
                      {video.tags.slice(0, 3).map((tag) => (
                        <span
                          key={tag}
                          className="shrink-0 rounded bg-muted px-1.5 py-0.5 text-xs text-muted-foreground max-w-[80px] truncate"
                        >
                          #{tag}
                        </span>
                      ))}
                    </div>
                  )}
                </div>
              )
              : (
                <div className="flex gap-3">
                  <Link
                    to="/$username/$channel"
                    params={{ username: video.ownerUsername, channel: video.channelHandle }}
                    onClick={(e) => e.stopPropagation()}
                    className="no-underline shrink-0"
                  >
                    <Avatar className="mt-0.5 h-8 w-8">
                      <AvatarImage src={video.channelAvatarUrl ?? undefined} alt={video.channelName ?? video.channelHandle} />
                      <AvatarFallback className="text-xs">{channelInitial}</AvatarFallback>
                    </Avatar>
                  </Link>

                  <div className="min-w-0 flex-1 space-y-1">
                    <h3 className="line-clamp-2 text-sm font-bold leading-tight text-foreground">
                      {video.title}
                    </h3>
                    <Link
                      to="/$username/$channel"
                      params={{ username: video.ownerUsername, channel: video.channelHandle }}
                      onClick={(e) => e.stopPropagation()}
                      className="no-underline"
                    >
                      <p className="text-xs text-muted-foreground hover:text-foreground transition-colors">
                        @{video.ownerUsername}/{video.channelHandle}
                      </p>
                    </Link>
                    <div className="flex items-center gap-2 text-xs text-muted-foreground">
                      <span className="flex items-center gap-1">
                        <Eye className="h-3 w-3" />
                        {viewCountFormatted}
                      </span>
                      <span className="flex items-center gap-1">
                        <ThumbsUp className="h-3 w-3" />
                        {likeCountFormatted}
                      </span>
                      {dislikeCountFormatted !== null && (
                        <span className="flex items-center gap-1">
                          <ThumbsDown className="h-3 w-3" />
                          {dislikeCountFormatted}
                        </span>
                      )}
                      <span>· <TimeAgo isoDate={video.createdAt} /></span>
                    </div>
                    {video.tags.length > 0 && (
                      <div className="flex min-w-0 gap-1 overflow-hidden">
                        {video.tags.slice(0, 3).map((tag) => (
                          <span
                            key={tag}
                            className="shrink-0 rounded bg-muted px-1.5 py-0.5 text-xs text-muted-foreground max-w-[80px] truncate"
                          >
                            #{tag}
                          </span>
                        ))}
                      </div>
                    )}
                  </div>
                </div>
              )}
          </CardContent>
        </Card>
      </Link>

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
