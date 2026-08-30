import { Link, createFileRoute, redirect } from '@tanstack/react-router'
import { Plus } from 'lucide-react'
import { useState } from 'react'
import { Avatar, AvatarFallback, AvatarImage } from '#/components/ui/avatar'
import { Button } from '#/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '#/components/ui/card'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from '#/components/ui/dialog'
import { Separator } from '#/components/ui/separator'
import { Skeleton } from '#/components/ui/skeleton'
import { CreateChannelForm } from '#/features/channels/components/CreateChannelForm'
import { useUserChannels } from '#/features/channels/hooks'
import type { ChannelSummary } from '#/features/channels/types'
import { AvatarUpload } from '#/features/users/components/AvatarUpload'
import { ProfileInfo } from '#/features/users/components/ProfileInfo'
import { useCurrentUser } from '#/features/users/hooks'
import { tokenStore } from '#/shared/lib/token-store'

export const Route = createFileRoute('/settings')({
  beforeLoad: () => {
    const isServer = typeof window === 'undefined'
    if (isServer) return

    const isAuthenticated = tokenStore.get() !== null
    if (!isAuthenticated) {
      throw redirect({ to: '/' })
    }
  },
  component: SettingsPage,
})

type ChannelRowProps = {
  channel: ChannelSummary
  username: string
}

function ChannelRow({ channel, username }: ChannelRowProps) {
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
      <div className="flex items-center gap-3 rounded-lg p-2 transition-colors hover:bg-muted">
        <Avatar className="h-10 w-10 shrink-0">
          <AvatarImage src={channel.avatarUrl ?? undefined} alt={channel.name} />
          <AvatarFallback>{channelInitial}</AvatarFallback>
        </Avatar>
        <div className="min-w-0 flex-1">
          <p className="truncate text-sm font-medium text-foreground">{channel.name}</p>
          <p className="text-xs text-muted-foreground">@{channel.handle} · {followerLabel}</p>
        </div>
      </div>
    </Link>
  )
}

function ChannelsSkeleton() {
  return (
    <div className="space-y-2">
      {Array.from({ length: 2 }).map((_, i) => (
        // biome-ignore lint/suspicious/noArrayIndexKey: skeleton placeholders have no identity
        <div key={i} className="flex items-center gap-3 p-2">
          <Skeleton className="h-10 w-10 rounded-full shrink-0" />
          <div className="flex-1 space-y-1.5">
            <Skeleton className="h-3.5 w-1/3" />
            <Skeleton className="h-3 w-1/2" />
          </div>
        </div>
      ))}
    </div>
  )
}

type ChannelsSectionProps = {
  username: string
}

function ChannelsSection({ username }: ChannelsSectionProps) {
  const [createOpen, setCreateOpen] = useState(false)
  const { data, isPending } = useUserChannels(username)

  const channels = data?.channels ?? []

  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between space-y-0 pb-3">
        <CardTitle>Channels</CardTitle>
        <Dialog open={createOpen} onOpenChange={setCreateOpen}>
          <DialogTrigger asChild>
            <Button variant="outline" size="sm">
              <Plus className="mr-1.5 h-4 w-4" />
              New channel
            </Button>
          </DialogTrigger>
          <DialogContent>
            <DialogHeader>
              <DialogTitle>Create channel</DialogTitle>
            </DialogHeader>
            <CreateChannelForm
              username={username}
              onSuccess={() => setCreateOpen(false)}
            />
          </DialogContent>
        </Dialog>
      </CardHeader>
      <CardContent>
        {isPending && <ChannelsSkeleton />}

        {!isPending && channels.length === 0 && (
          <p className="py-2 text-sm text-muted-foreground">
            You have no channels yet.
          </p>
        )}

        {!isPending && channels.length > 0 && (
          <div className="space-y-1">
            {channels.map((channel) => (
              <ChannelRow key={channel.channelId} channel={channel} username={username} />
            ))}
          </div>
        )}
      </CardContent>
    </Card>
  )
}

function SettingsPage() {
  const { data: profile, isPending, isError, error } = useCurrentUser()

  if (isPending) {
    return <main className="page-container py-8"><p>Loading…</p></main>
  }

  if (isError) {
    return (
      <main className="page-container py-8">
        <p className="text-destructive">{error.message}</p>
      </main>
    )
  }

  return (
    <main className="page-container py-8">
      <h1 className="mb-6 text-2xl font-bold">Settings</h1>

      <div className="max-w-lg space-y-6">
        <Card>
          <CardHeader>
            <CardTitle>Avatar</CardTitle>
          </CardHeader>
          <CardContent>
            <AvatarUpload profile={profile} />
          </CardContent>
        </Card>

        <Separator />

        <Card>
          <CardHeader>
            <CardTitle>Profile</CardTitle>
          </CardHeader>
          <CardContent>
            <ProfileInfo profile={profile} />
          </CardContent>
        </Card>

        <Separator />

        <ChannelsSection username={profile.username} />
      </div>
    </main>
  )
}
