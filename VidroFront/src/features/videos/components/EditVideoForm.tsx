import { zodResolver } from '@hookform/resolvers/zod'
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
import { getApiErrorMessage } from '#/shared/lib/error-messages'
import { useUpdateVideo } from '../hooks'
import type { ChannelVideoSummary } from '../types'

// ─── Constants ───────────────────────────────────────────────────────────────

const VISIBILITY_OPTIONS = [
  { label: 'Public', value: '0' },
  { label: 'Unlisted', value: '1' },
  { label: 'Private', value: '2' },
]

const MAX_TAGS = 10

// ─── Schema ──────────────────────────────────────────────────────────────────

const editVideoSchema = z.object({
  title: z.string().min(1, 'Title is required').max(100),
  description: z.string().max(5000).optional(),
  tags: z.string().optional(),
  visibility: z.string(),
})

type EditVideoFormValues = z.infer<typeof editVideoSchema>

// ─── Props ───────────────────────────────────────────────────────────────────

type Props = {
  video: ChannelVideoSummary
  onSuccess: () => void
}

// ─── Component ───────────────────────────────────────────────────────────────

export function EditVideoForm({ video, onSuccess }: Props) {
  const updateVideo = useUpdateVideo(video.videoId)

  const {
    register,
    handleSubmit,
    setValue,
    watch,
    formState: { errors },
  } = useForm<EditVideoFormValues>({
    resolver: zodResolver(editVideoSchema),
    defaultValues: {
      title: video.title,
      description: video.description ?? '',
      tags: video.tags.join(', '),
      visibility: String(video.visibility.id),
    },
  })

  const visibilityValue = watch('visibility')

  async function onSubmit(values: EditVideoFormValues) {
    const rawTags = values.tags ?? ''
    const parsedTags = rawTags
      .split(',')
      .map((t) => t.trim())
      .filter(Boolean)
      .slice(0, MAX_TAGS)

    await updateVideo.mutateAsync(
      {
        title: values.title,
        description: values.description || null,
        tags: parsedTags,
        visibility: Number(values.visibility),
      },
      { onSuccess },
    )
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-5">
      {/* Title */}
      <div className="space-y-2">
        <Label htmlFor="edit-title">Title</Label>
        <Input id="edit-title" {...register('title')} placeholder="Enter video title" />
        {errors.title && (
          <p className="text-sm text-destructive">{errors.title.message}</p>
        )}
      </div>

      {/* Description */}
      <div className="space-y-2">
        <Label htmlFor="edit-description">Description</Label>
        <Textarea
          id="edit-description"
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
        <Label htmlFor="edit-tags">Tags</Label>
        <Input
          id="edit-tags"
          {...register('tags')}
          placeholder="gaming, tutorial, react (comma-separated)"
        />
        <p className="text-xs text-muted-foreground">Up to {MAX_TAGS} tags, separated by commas</p>
      </div>

      {/* Visibility */}
      <div className="space-y-2">
        <Label htmlFor="edit-visibility">Visibility</Label>
        <Select
          value={visibilityValue}
          onValueChange={(val) => setValue('visibility', val)}
        >
          <SelectTrigger id="edit-visibility">
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

      {updateVideo.error && (
        <p className="text-sm text-destructive">{getApiErrorMessage(updateVideo.error)}</p>
      )}

      <Button type="submit" disabled={updateVideo.isPending} className="w-full">
        {updateVideo.isPending ? 'Saving…' : 'Save changes'}
      </Button>
    </form>
  )
}
