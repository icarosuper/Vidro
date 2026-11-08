import { Link } from '@tanstack/react-router'
import { Camera, Pencil } from 'lucide-react'
import { useRef, useState } from 'react'
import { Avatar, AvatarFallback, AvatarImage } from '#/components/ui/avatar'
import { Button } from '#/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from '#/components/ui/dialog'
import { Skeleton } from '#/components/ui/skeleton'
import { useUploadChannelAvatar } from '../hooks'
import type { Channel } from '../types'
import { EditChannelForm } from './EditChannelForm'
import { SubscribeButton } from './SubscribeButton'

// The API has no banner image, so every channel gets the same accent gradient as its cover.
function ChannelCover() {
  return (
    <div
      aria-hidden
      className="h-28 rounded-lg bg-linear-to-br from-primary/60 via-primary/25 to-accent sm:h-40"
    />
  )
}

export function ChannelHeaderSkeleton() {
  return (
    <header>
      <ChannelCover />
      <div className="-mt-10 flex items-end gap-4 px-4 sm:-mt-12 sm:px-6">
        <Skeleton className="size-20 shrink-0 rounded-xl ring-4 ring-background sm:size-24" />
        <div className="flex-1 space-y-2 pb-1">
          <Skeleton className="h-7 w-48" />
          <Skeleton className="h-3.5 w-64" />
        </div>
      </div>
    </header>
  )
}

type ChannelHeaderProps = {
  channel: Channel
  isOwner: boolean
  canSubscribe: boolean
}

export function ChannelHeader({
  channel,
  isOwner,
  canSubscribe,
}: ChannelHeaderProps) {
  const [editOpen, setEditOpen] = useState(false)
  const avatarInputRef = useRef<HTMLInputElement>(null)
  const uploadAvatar = useUploadChannelAvatar(
    channel.ownerUsername,
    channel.handle,
  )

  const channelInitial = channel.name.charAt(0).toUpperCase()
  const ownerInitial = channel.ownerUsername.charAt(0).toUpperCase()
  const followerLabel =
    channel.followerCount === 1
      ? '1 follower'
      : `${channel.followerCount} followers`

  function handleAvatarFileChange(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    if (!file) return
    uploadAvatar.mutate(file)
    e.target.value = ''
  }

  return (
    <header>
      <ChannelCover />

      <div className="flex flex-col gap-4 px-4 sm:flex-row sm:items-end sm:px-6">
        {/* The avatar overlaps the cover's bottom edge. */}
        <div className="relative -mt-10 size-20 shrink-0 sm:-mt-12 sm:size-24">
          <Avatar className="size-full rounded-xl ring-4 ring-background">
            <AvatarImage
              src={channel.avatarUrl ?? undefined}
              alt={channel.name}
            />
            <AvatarFallback className="rounded-xl text-3xl">
              {channelInitial}
            </AvatarFallback>
          </Avatar>
          {isOwner && (
            <>
              <button
                type="button"
                onClick={() => avatarInputRef.current?.click()}
                disabled={uploadAvatar.isPending}
                aria-label="Change avatar"
                className="absolute inset-0 flex items-center justify-center rounded-xl bg-black/50 opacity-0 transition-opacity hover:opacity-100 focus-visible:opacity-100 disabled:cursor-not-allowed"
              >
                <Camera className="size-6 text-white" />
              </button>
              <input
                ref={avatarInputRef}
                type="file"
                accept="image/*"
                className="hidden"
                onChange={handleAvatarFileChange}
              />
            </>
          )}
        </div>

        <div className="min-w-0 flex-1">
          <h1 className="truncate text-3xl leading-tight font-extrabold tracking-tight">
            {channel.name}
          </h1>
          <p className="mt-1 font-mono text-xs text-muted-foreground">
            @{channel.ownerUsername}/{channel.handle} · {followerLabel}
          </p>
        </div>

        <div className="flex shrink-0 items-center gap-2">
          {isOwner && (
            <Button variant="outline" onClick={() => setEditOpen(true)}>
              <Pencil /> Edit channel
            </Button>
          )}
          {canSubscribe && (
            <SubscribeButton
              username={channel.ownerUsername}
              handle={channel.handle}
              initialIsFollowing={channel.isFollowing}
            />
          )}
        </div>
      </div>

      <div className="mt-4 space-y-3 px-4 sm:px-6">
        {channel.description && (
          <p className="max-w-3xl text-sm leading-relaxed text-foreground/90">
            {channel.description}
          </p>
        )}
        <Link
          to="/$username"
          params={{ username: channel.ownerUsername }}
          className="group inline-flex items-center gap-2 no-underline"
        >
          <Avatar className="size-6">
            <AvatarImage
              src={channel.ownerAvatarUrl ?? undefined}
              alt={channel.ownerUsername}
            />
            <AvatarFallback className="text-xs">{ownerInitial}</AvatarFallback>
          </Avatar>
          <span className="text-sm text-muted-foreground transition-colors group-hover:text-foreground">
            by <span className="font-semibold">{channel.ownerUsername}</span>
          </span>
        </Link>
      </div>

      <Dialog open={editOpen} onOpenChange={setEditOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Edit channel</DialogTitle>
          </DialogHeader>
          <EditChannelForm
            channel={channel}
            onSuccess={() => setEditOpen(false)}
          />
        </DialogContent>
      </Dialog>
    </header>
  )
}
