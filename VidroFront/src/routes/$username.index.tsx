import { Link, createFileRoute } from '@tanstack/react-router'
import { Users } from 'lucide-react'
import { Avatar, AvatarFallback, AvatarImage } from '#/components/ui/avatar'
import { Card, CardContent } from '#/components/ui/card'
import { Skeleton } from '#/components/ui/skeleton'
import { useUserChannels } from '#/features/channels/hooks'
import type { ChannelSummary } from '#/features/channels/types'

export const Route = createFileRoute('/$username/')({
  component: UserPage,
})

function ChannelCardSkeleton() {
  return (
    <div className="rounded-lg border border-border p-4">
      <div className="flex items-center gap-4">
        <Skeleton className="h-14 w-14 rounded-full" />
        <div className="flex-1 space-y-2">
          <Skeleton className="h-4 w-1/3" />
          <Skeleton className="h-3 w-1/4" />
          <Skeleton className="h-3 w-1/2" />
        </div>
      </div>
    </div>
  )
}

type ChannelCardProps = {
  channel: ChannelSummary
  username: string
}

function ChannelCard({ channel, username }: ChannelCardProps) {
  const channelInitial = channel.name.charAt(0).toUpperCase()
  const followerLabel = channel.followerCount === 1
    ? '1 follower'
    : `${channel.followerCount} followers`

  return (
    <Link
      to="/$username/$channel"
      params={{ username, channel: channel.handle }}
      className="no-underline"
    >
      <Card className="transition-shadow hover:shadow-md">
        <CardContent className="p-4">
          <div className="flex items-center gap-4">
            <Avatar className="h-14 w-14 shrink-0">
              <AvatarImage src={channel.avatarUrl ?? undefined} alt={channel.name} />
              <AvatarFallback className="text-lg">{channelInitial}</AvatarFallback>
            </Avatar>

            <div className="min-w-0 flex-1">
              <h2 className="truncate text-base font-semibold text-foreground">{channel.name}</h2>
              <p className="text-sm text-muted-foreground">@{channel.handle}</p>
              <p className="mt-1 flex items-center gap-1 text-xs text-muted-foreground">
                <Users className="h-3 w-3" />
                {followerLabel}
              </p>
            </div>
          </div>

          {channel.description && (
            <p className="mt-3 line-clamp-2 text-sm text-muted-foreground">
              {channel.description}
            </p>
          )}
        </CardContent>
      </Card>
    </Link>
  )
}

function UserPage() {
  const { username } = Route.useParams()
  const { data, isPending, isError, error } = useUserChannels(username)

  if (isError) {
    return (
      <main className="page-container py-8">
        <p className="text-destructive">{error.message}</p>
      </main>
    )
  }

  const channels = data?.channels ?? []
  const hasNoChannels = !isPending && channels.length === 0

  return (
    <main className="page-container py-8">
      <h1 className="mb-6 text-2xl font-bold">{username}'s channels</h1>

      {isPending && (
        <div className="space-y-4">
          {Array.from({ length: 3 }).map((_, i) => (
            // biome-ignore lint/suspicious/noArrayIndexKey: skeleton placeholders have no identity
            <ChannelCardSkeleton key={i} />
          ))}
        </div>
      )}

      {hasNoChannels && (
        <p className="py-8 text-center text-muted-foreground">
          This user has no channels yet.
        </p>
      )}

      {!isPending && channels.length > 0 && (
        <div className="space-y-4">
          {channels.map((channel) => (
            <ChannelCard key={channel.channelId} channel={channel} username={username} />
          ))}
        </div>
      )}
    </main>
  )
}
