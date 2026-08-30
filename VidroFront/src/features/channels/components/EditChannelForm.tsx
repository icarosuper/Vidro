import { zodResolver } from '@hookform/resolvers/zod'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { Button } from '#/components/ui/button'
import { Input } from '#/components/ui/input'
import { Label } from '#/components/ui/label'
import { Textarea } from '#/components/ui/textarea'
import { getApiErrorMessage } from '#/shared/lib/error-messages'
import { useUpdateChannel } from '../hooks'
import type { Channel } from '../types'

type Props = {
  channel: Channel
  onSuccess: () => void
}

const NAME_MAX = 100
const DESCRIPTION_MAX = 500

const schema = z.object({
  name: z
    .string()
    .min(1, 'Name is required')
    .max(NAME_MAX, `Name must be at most ${NAME_MAX} characters`),
  description: z
    .string()
    .max(DESCRIPTION_MAX, `Description must be at most ${DESCRIPTION_MAX} characters`)
    .optional(),
})

type FormValues = z.infer<typeof schema>

export function EditChannelForm({ channel, onSuccess }: Props) {
  const updateChannel = useUpdateChannel(channel.ownerUsername, channel.handle)

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: channel.name,
      description: channel.description ?? '',
    },
  })

  function onSubmit(values: FormValues) {
    updateChannel.mutate(
      {
        name: values.name,
        description: values.description?.trim() || null,
      },
      { onSuccess },
    )
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
      <div className="space-y-2">
        <Label htmlFor="edit-channel-name">Name</Label>
        <Input id="edit-channel-name" {...register('name')} />
        {errors.name && (
          <p className="text-sm text-destructive">{errors.name.message}</p>
        )}
      </div>

      <div className="space-y-2">
        <Label htmlFor="edit-channel-description">Description</Label>
        <Textarea
          id="edit-channel-description"
          {...register('description')}
          rows={4}
          placeholder="What is your channel about? (optional)"
        />
        {errors.description && (
          <p className="text-sm text-destructive">{errors.description.message}</p>
        )}
      </div>

      {updateChannel.error && (
        <p className="text-sm text-destructive">{getApiErrorMessage(updateChannel.error)}</p>
      )}

      <Button type="submit" disabled={updateChannel.isPending}>
        {updateChannel.isPending ? 'Saving…' : 'Save changes'}
      </Button>
    </form>
  )
}
