import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect, useRef, useState } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { Button } from '#/components/ui/button'
import { Input } from '#/components/ui/input'
import { Label } from '#/components/ui/label'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '#/components/ui/select'
import { Textarea } from '#/components/ui/textarea'
import { CreateChannelForm } from '#/features/channels/components/CreateChannelForm'
import { useUserChannels } from '#/features/channels/hooks'
import { getApiErrorMessage } from '#/shared/lib/error-messages'
import { VideoStatus } from '#/shared/types'
import { useCreateVideo, useUpdateVideo, useUploadThumbnail, useVideoStatus } from '../hooks'
import { uploadVideoFile } from '../api'

// ─── Types ──────────────────────────────────────────────────────────────────

type UploadStage =
  | { kind: 'form' }
  | { kind: 'uploading'; videoId: string; progress: number }
  | { kind: 'processing'; videoId: string }
  | { kind: 'done'; videoId: string }
  | { kind: 'failed'; error: string }

type Props = {
  username: string
}

// ─── Schema ─────────────────────────────────────────────────────────────────

const VISIBILITY_OPTIONS = [
  { label: 'Public', value: '0' },
  { label: 'Unlisted', value: '1' },
  { label: 'Private', value: '2' },
]

const MAX_TAGS = 10
const ACCEPTED_VIDEO_TYPES = 'video/*'
const ACCEPTED_IMAGE_TYPES = 'image/jpeg,image/png,image/webp'

const uploadSchema = z.object({
  channelId: z.string().min(1, 'Select a channel'),
  title: z.string().min(1, 'Title is required').max(100),
  description: z.string().max(5000).optional(),
  tags: z.string().optional(),
  visibility: z.string(),
})

type UploadFormValues = z.infer<typeof uploadSchema>

// ─── Sub-components ──────────────────────────────────────────────────────────

function UploadProgress({ progress }: { progress: number }) {
  return (
    <div className="space-y-3 text-center">
      <p className="text-lg font-medium">Uploading…</p>
      <div className="h-3 w-full overflow-hidden rounded-full bg-muted">
        <div
          className="h-full bg-primary transition-all duration-300"
          style={{ width: `${progress}%` }}
        />
      </div>
      <p className="text-sm text-muted-foreground">{progress}%</p>
    </div>
  )
}

function ProcessingStatus({ videoId }: { videoId: string }) {
  const { data: video } = useVideoStatus(videoId)
  const status = video?.status

  const hasFailed = status?.id === VideoStatus.Failed
  const isReady = status?.id === VideoStatus.Ready

  if (hasFailed) {
    return (
      <div className="space-y-2 text-center">
        <p className="text-destructive text-lg font-medium">Processing failed</p>
        <p className="text-sm text-muted-foreground">
          Something went wrong while processing your video. Please try again.
        </p>
      </div>
    )
  }

  if (isReady) {
    return null
  }

  return (
    <div className="space-y-3 text-center">
      <div className="mx-auto h-8 w-8 animate-spin rounded-full border-4 border-muted border-t-primary" />
      <p className="text-lg font-medium">Processing video…</p>
      <p className="text-sm text-muted-foreground">
        This may take a few minutes. You can leave this page.
      </p>
    </div>
  )
}

function DoneState({ videoId }: { videoId: string }) {
  const thumbnailInputRef = useRef<HTMLInputElement>(null)
  const uploadThumbnail = useUploadThumbnail(videoId)

  function handleThumbnailChange(event: React.ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    if (!file) return
    uploadThumbnail.mutate(file)
    event.target.value = ''
  }

  return (
    <div className="space-y-6 text-center">
      <div className="space-y-2">
        <p className="text-2xl font-bold">Upload complete!</p>
        <p className="text-muted-foreground">Your video is ready.</p>
      </div>

      <div className="flex flex-col gap-3">
        <Button asChild>
          <a href={`/watch/${videoId}`}>Watch your video</a>
        </Button>

        <div className="flex flex-col gap-2">
          <input
            ref={thumbnailInputRef}
            type="file"
            accept={ACCEPTED_IMAGE_TYPES}
            onChange={handleThumbnailChange}
            className="hidden"
          />
          <Button
            variant="outline"
            onClick={() => thumbnailInputRef.current?.click()}
            disabled={uploadThumbnail.isPending}
          >
            {uploadThumbnail.isPending ? 'Uploading thumbnail…' : 'Upload custom thumbnail'}
          </Button>
          {uploadThumbnail.isSuccess && (
            <p className="text-sm text-green-600">Thumbnail uploaded!</p>
          )}
          {uploadThumbnail.error && (
            <p className="text-sm text-destructive">{getApiErrorMessage(uploadThumbnail.error)}</p>
          )}
        </div>
      </div>
    </div>
  )
}

// ─── Main component ──────────────────────────────────────────────────────────

export function UploadVideoForm({ username }: Props) {
  const [stage, setStage] = useState<UploadStage>({ kind: 'form' })
  const [videoFile, setVideoFile] = useState<File | null>(null)
  const fileInputRef = useRef<HTMLInputElement>(null)

  const { data: channelsData, isPending: channelsPending } = useUserChannels(username)
  const channels = channelsData?.channels ?? []

  const {
    register,
    handleSubmit,
    setValue,
    watch,
    formState: { errors },
  } = useForm<UploadFormValues>({
    resolver: zodResolver(uploadSchema),
    defaultValues: { visibility: '0' },
  })

  const selectedChannelId = watch('channelId')
  const selectedChannel = channels.find((c) => c.channelId === selectedChannelId)

  useEffect(() => {
    const firstChannel = channels[0]
    const noChannelSelected = !selectedChannelId && firstChannel
    if (noChannelSelected) {
      setValue('channelId', firstChannel.channelId)
    }
  }, [channels, selectedChannelId, setValue])
  const createVideo = useCreateVideo(username, selectedChannel?.handle ?? '')

  function handleFileChange(event: React.ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    if (!file) return
    setVideoFile(file)
  }

  async function onSubmit(values: UploadFormValues) {
    const fileIsSelected = videoFile !== null
    if (!fileIsSelected) {
      return
    }

    const rawTags = values.tags ?? ''
    const parsedTags = rawTags
      .split(',')
      .map((t) => t.trim())
      .filter(Boolean)
      .slice(0, MAX_TAGS)

    let createResponse: Awaited<ReturnType<typeof createVideo.mutateAsync>>

    try {
      createResponse = await createVideo.mutateAsync({
        title: values.title,
        description: values.description ?? null,
        tags: parsedTags,
        visibility: Number(values.visibility),
      })
    } catch (err) {
      setStage({ kind: 'failed', error: getApiErrorMessage(err) })
      return
    }

    const { videoId, uploadUrl } = createResponse

    setStage({ kind: 'uploading', videoId, progress: 0 })

    try {
      await uploadVideoFile(uploadUrl, videoFile, (progress) => {
        setStage({ kind: 'uploading', videoId, progress })
      })
    } catch (err) {
      const message = err instanceof Error
        ? err.message
        : 'File upload failed'
      setStage({ kind: 'failed', error: message })
      return
    }

    setStage({ kind: 'processing', videoId })
  }

  // Transition processing → done once video is ready
  function handleProcessingDone(videoId: string) {
    setStage({ kind: 'done', videoId })
  }

  // ─── Render stages ────────────────────────────────────────────────────────

  if (stage.kind === 'uploading') {
    return (
      <div className="max-w-md mx-auto py-12">
        <UploadProgress progress={stage.progress} />
      </div>
    )
  }

  if (stage.kind === 'processing') {
    return (
      <div className="max-w-md mx-auto py-12">
        <ProcessingStatusWatcher
          videoId={stage.videoId}
          onReady={() => handleProcessingDone(stage.videoId)}
        />
      </div>
    )
  }

  if (stage.kind === 'done') {
    return (
      <div className="max-w-md mx-auto py-12">
        <DoneState videoId={stage.videoId} />
      </div>
    )
  }

  if (stage.kind === 'failed') {
    return (
      <div className="max-w-md mx-auto py-12 space-y-4 text-center">
        <p className="text-destructive text-lg font-medium">Upload failed</p>
        <p className="text-sm text-muted-foreground">{stage.error}</p>
        <Button variant="outline" onClick={() => setStage({ kind: 'form' })}>
          Try again
        </Button>
      </div>
    )
  }

  // ─── Form ─────────────────────────────────────────────────────────────────

  const noChannels = !channelsPending && channels.length === 0

  if (noChannels) {
    return (
      <div className="max-w-lg space-y-4">
        <div className="rounded-lg border border-border bg-muted/40 p-4">
          <p className="text-sm text-muted-foreground">
            You need a channel before you can upload videos.
          </p>
        </div>
        <CreateChannelForm username={username} />
      </div>
    )
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="max-w-lg space-y-6">
      {/* Channel */}
      <div className="space-y-2">
        <Label htmlFor="channel">Channel</Label>
        {channelsPending
          ? <p className="text-sm text-muted-foreground">Loading channels…</p>
          : (
              <Select value={selectedChannelId} onValueChange={(val) => setValue('channelId', val)}>
                <SelectTrigger id="channel">
                  <SelectValue placeholder="Select a channel" />
                </SelectTrigger>
                <SelectContent>
                  {channels.map((channel) => (
                    <SelectItem key={channel.channelId} value={channel.channelId}>
                      {channel.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )
        }
        {errors.channelId && (
          <p className="text-sm text-destructive">{errors.channelId.message}</p>
        )}
      </div>

      {/* Video file */}
      <div className="space-y-2">
        <Label>Video file</Label>
        <div className="flex items-center gap-3">
          <input
            ref={fileInputRef}
            type="file"
            accept={ACCEPTED_VIDEO_TYPES}
            onChange={handleFileChange}
            className="hidden"
          />
          <Button
            type="button"
            variant="outline"
            onClick={() => fileInputRef.current?.click()}
          >
            {videoFile ? 'Change file' : 'Select file'}
          </Button>
          {videoFile && (
            <span className="text-sm text-muted-foreground truncate max-w-[240px]">
              {videoFile.name}
            </span>
          )}
        </div>
        {!videoFile && createVideo.isError && (
          <p className="text-sm text-destructive">Please select a video file</p>
        )}
      </div>

      {/* Title */}
      <div className="space-y-2">
        <Label htmlFor="title">Title</Label>
        <Input id="title" {...register('title')} placeholder="Enter video title" />
        {errors.title && (
          <p className="text-sm text-destructive">{errors.title.message}</p>
        )}
      </div>

      {/* Description */}
      <div className="space-y-2">
        <Label htmlFor="description">Description</Label>
        <Textarea
          id="description"
          {...register('description')}
          placeholder="Describe your video (optional)"
          rows={4}
        />
        {errors.description && (
          <p className="text-sm text-destructive">{errors.description.message}</p>
        )}
      </div>

      {/* Tags */}
      <div className="space-y-2">
        <Label htmlFor="tags">Tags</Label>
        <Input
          id="tags"
          {...register('tags')}
          placeholder="gaming, tutorial, react (comma-separated)"
        />
        <p className="text-xs text-muted-foreground">Up to {MAX_TAGS} tags, separated by commas</p>
      </div>

      {/* Visibility */}
      <div className="space-y-2">
        <Label htmlFor="visibility">Visibility</Label>
        <Select
          defaultValue="0"
          onValueChange={(val) => setValue('visibility', val)}
        >
          <SelectTrigger id="visibility">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {VISIBILITY_OPTIONS.map((opt) => (
              <SelectItem key={opt.value} value={opt.value}>
                {opt.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      {createVideo.error && (
        <p className="text-sm text-destructive">{getApiErrorMessage(createVideo.error)}</p>
      )}

      <Button
        type="submit"
        disabled={createVideo.isPending || !videoFile}
        className="w-full"
      >
        {createVideo.isPending ? 'Starting upload…' : 'Upload video'}
      </Button>
    </form>
  )
}

// ─── Processing watcher ──────────────────────────────────────────────────────

// Separate component so it can call useVideoStatus for polling
function ProcessingStatusWatcher({
  videoId,
  onReady,
}: {
  videoId: string
  onReady: () => void
}) {
  const { data: video } = useVideoStatus(videoId)
  const status = video?.status

  const hasFailed = status?.id === VideoStatus.Failed
  const isReady = status?.id === VideoStatus.Ready

  useEffect(() => {
    if (isReady) onReady()
  }, [isReady, onReady])

  if (hasFailed) {
    return (
      <div className="space-y-2 text-center">
        <p className="text-destructive text-lg font-medium">Processing failed</p>
        <p className="text-sm text-muted-foreground">
          Something went wrong while processing your video. Please try again.
        </p>
      </div>
    )
  }

  return <ProcessingStatus videoId={videoId} />
}
