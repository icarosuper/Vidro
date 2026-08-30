import { useEffect, useRef, useState } from 'react'
import { Button } from '#/components/ui/button'
import { ApiClientError } from '#/shared/lib/api-client'
import { useFollowChannel, useUnfollowChannel } from '../hooks'

type Props = {
  username: string
  handle: string
  initialIsFollowing?: boolean
}

export function SubscribeButton({ username, handle, initialIsFollowing = false }: Props) {
  const [isFollowing, setIsFollowing] = useState(initialIsFollowing)
  const hasInteracted = useRef(false)

  useEffect(() => {
    const shouldSync = !hasInteracted.current
    if (shouldSync) {
      setIsFollowing(initialIsFollowing)
    }
  }, [initialIsFollowing])

  const follow = useFollowChannel(username, handle)
  const unfollow = useUnfollowChannel(username, handle)

  const isMutating = follow.isPending || unfollow.isPending

  function handleFollow() {
    hasInteracted.current = true
    follow.mutate(undefined, {
      onSuccess: () => setIsFollowing(true),
      onError: (error) => {
        const isAlreadyFollowing =
          error instanceof ApiClientError && error.code === 'channel.already_following'
        if (isAlreadyFollowing) setIsFollowing(true)
      },
    })
  }

  function handleUnfollow() {
    hasInteracted.current = true
    unfollow.mutate(undefined, {
      onSuccess: () => setIsFollowing(false),
      onError: (error) => {
        const isNotFollowing =
          error instanceof ApiClientError && error.code === 'channel.not_following'
        if (isNotFollowing) setIsFollowing(false)
      },
    })
  }

  if (isFollowing) {
    return (
      <Button
        variant="outline"
        size="sm"
        onClick={handleUnfollow}
        disabled={isMutating}
      >
        {unfollow.isPending ? 'Unsubscribing…' : 'Subscribed'}
      </Button>
    )
  }

  return (
    <Button
      variant="default"
      size="sm"
      onClick={handleFollow}
      disabled={isMutating}
    >
      {follow.isPending ? 'Subscribing…' : 'Subscribe'}
    </Button>
  )
}
