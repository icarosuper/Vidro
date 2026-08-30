import { zodResolver } from '@hookform/resolvers/zod'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { Button } from '#/components/ui/button'
import { Input } from '#/components/ui/input'
import { Label } from '#/components/ui/label'
import { Textarea } from '#/components/ui/textarea'
import { getApiErrorMessage } from '#/shared/lib/error-messages'
import { useCreateChannel } from '../hooks'

type Props = {
  username: string | undefined
  onSuccess?: () => void
}

const HANDLE_MIN = 3
const HANDLE_MAX = 50
const NAME_MAX = 100
const DESCRIPTION_MAX = 500

const schema = z.object({
  handle: z
    .string()
    .min(HANDLE_MIN, `Handle must be at least ${HANDLE_MIN} characters`)
    .max(HANDLE_MAX, `Handle must be at most ${HANDLE_MAX} characters`)
    .regex(/^[a-z0-9-]+$/, 'Handle may only contain lowercase letters, digits, and hyphens'),
  name: z
    .string()
    .min(1, 'Name is required')
    .max(NAME_MAX, `Name must be at most ${NAME_MAX} characters`),
  description: z.string().max(DESCRIPTION_MAX).optional(),
})

type FormValues = z.infer<typeof schema>

export function CreateChannelForm({ username, onSuccess }: Props) {
  const createChannel = useCreateChannel(username)

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<FormValues>({ resolver: zodResolver(schema) })

  function onSubmit(values: FormValues) {
    createChannel.mutate(
      {
        handle: values.handle,
        name: values.name,
        description: values.description ?? null,
      },
      { onSuccess },
    )
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
      <div className="space-y-2">
        <Label htmlFor="channel-name">Name</Label>
        <Input
          id="channel-name"
          {...register('name')}
          placeholder="My Channel"
        />
        {errors.name && (
          <p className="text-sm text-destructive">{errors.name.message}</p>
        )}
      </div>

      <div className="space-y-2">
        <Label htmlFor="channel-handle">Handle</Label>
        <Input
          id="channel-handle"
          {...register('handle')}
          placeholder="my-channel"
        />
        <p className="text-xs text-muted-foreground">
          Lowercase letters, digits, and hyphens only
        </p>
        {errors.handle && (
          <p className="text-sm text-destructive">{errors.handle.message}</p>
        )}
      </div>

      <div className="space-y-2">
        <Label htmlFor="channel-description">Description</Label>
        <Textarea
          id="channel-description"
          {...register('description')}
          placeholder="What is your channel about? (optional)"
          rows={3}
        />
        {errors.description && (
          <p className="text-sm text-destructive">{errors.description.message}</p>
        )}
      </div>

      {createChannel.error && (
        <p className="text-sm text-destructive">{getApiErrorMessage(createChannel.error)}</p>
      )}

      <Button type="submit" disabled={createChannel.isPending}>
        {createChannel.isPending ? 'Creating…' : 'Create channel'}
      </Button>
    </form>
  )
}
