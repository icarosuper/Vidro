import { Bookmark, BookmarkCheck, Lock, Plus } from 'lucide-react'
import { useState } from 'react'
import { Button } from '#/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from '#/components/ui/dialog'
import {
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '#/components/ui/dropdown-menu'
import { Skeleton } from '#/components/ui/skeleton'
import { useAuthModal, useIsAuthenticated } from '#/features/auth/hooks'
import { useCurrentUser } from '#/features/users/hooks'
import { PlaylistVisibility } from '#/shared/types'
import {
  type SaveTarget,
  useSaveTargets,
  useToggleVideoInPlaylist,
} from '../hooks'
import { CreatePlaylistForm } from './CreatePlaylistForm'

type Props = {
  videoId: string
  // The video's channel handle when the viewer owns it — its playlists are offered too.
  ownChannelHandle?: string
}

const TRIGGER_CLASS = 'rounded-full'

// "Save" on the watch page: a menu of the viewer's playlists, each a checkbox that adds or
// removes the video on the spot, and "New playlist", which creates one with the video in it.
export function SaveToPlaylistButton({ videoId, ownChannelHandle }: Props) {
  const isAuthenticated = useIsAuthenticated()
  const { openSignIn } = useAuthModal()
  const { data: currentUser } = useCurrentUser()
  const [createOpen, setCreateOpen] = useState(false)
  const targets = useSaveTargets({
    videoId,
    username: currentUser?.username,
    ownChannelHandle,
  })
  const toggle = useToggleVideoInPlaylist(videoId)

  if (!isAuthenticated) {
    return (
      <Button variant="outline" className={TRIGGER_CLASS} onClick={openSignIn}>
        <Bookmark /> Save
      </Button>
    )
  }

  const savedSomewhere =
    targets.data?.some((target) => target.containsVideo) ?? false
  const TriggerIcon = savedSomewhere ? BookmarkCheck : Bookmark

  function handleCreated(playlistId: string) {
    setCreateOpen(false)
    toggle.mutate({ playlistId, save: true })
  }

  return (
    <>
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button variant="outline" className={TRIGGER_CLASS}>
            <TriggerIcon className={savedSomewhere ? 'text-primary' : ''} />
            {savedSomewhere ? 'Saved' : 'Save'}
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end" className="w-64">
          <DropdownMenuLabel>Save to…</DropdownMenuLabel>
          <SaveTargetList
            targets={targets.data}
            isPending={targets.isPending}
            isError={targets.isError}
            onRetry={() => targets.refetch()}
            onToggle={(playlistId, save) => toggle.mutate({ playlistId, save })}
          />
          <DropdownMenuSeparator />
          <DropdownMenuItem onSelect={() => setCreateOpen(true)}>
            <Plus /> New playlist
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>

      <Dialog open={createOpen} onOpenChange={setCreateOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>New playlist</DialogTitle>
          </DialogHeader>
          <CreatePlaylistForm onSuccess={handleCreated} />
        </DialogContent>
      </Dialog>
    </>
  )
}

type SaveTargetListProps = {
  targets: SaveTarget[] | undefined
  isPending: boolean
  isError: boolean
  onRetry: () => void
  onToggle: (playlistId: string, save: boolean) => void
}

function SaveTargetList({
  targets,
  isPending,
  isError,
  onRetry,
  onToggle,
}: SaveTargetListProps) {
  if (isPending) {
    return (
      <div className="space-y-2 px-2 py-1.5">
        <Skeleton className="h-4 w-3/4" />
        <Skeleton className="h-4 w-1/2" />
      </div>
    )
  }

  if (isError || !targets) {
    return (
      <DropdownMenuItem
        onSelect={(event) => {
          event.preventDefault()
          onRetry()
        }}
        className="text-destructive"
      >
        Could not load your playlists. Try again
      </DropdownMenuItem>
    )
  }

  if (targets.length === 0) {
    return (
      <p className="px-2 py-1.5 text-sm text-muted-foreground">
        No playlists yet.
      </p>
    )
  }

  const personal = targets.filter((target) => target.group === 'personal')
  const channel = targets.filter((target) => target.group === 'channel')
  const showsGroups = channel.length > 0

  return (
    <>
      {showsGroups && personal.length > 0 && (
        <GroupLabel>Your playlists</GroupLabel>
      )}
      {personal.map((target) => (
        <SaveTargetItem key={target.id} target={target} onToggle={onToggle} />
      ))}
      {showsGroups && <GroupLabel>This channel</GroupLabel>}
      {channel.map((target) => (
        <SaveTargetItem key={target.id} target={target} onToggle={onToggle} />
      ))}
    </>
  )
}

function GroupLabel({ children }: { children: string }) {
  return (
    <DropdownMenuLabel className="pt-2 pb-1 font-mono text-[11px] font-medium text-muted-foreground uppercase">
      {children}
    </DropdownMenuLabel>
  )
}

type SaveTargetItemProps = {
  target: SaveTarget
  onToggle: (playlistId: string, save: boolean) => void
}

function SaveTargetItem({ target, onToggle }: SaveTargetItemProps) {
  const isPrivate = target.visibility.id === PlaylistVisibility.Private
  return (
    <DropdownMenuCheckboxItem
      checked={target.containsVideo === true}
      // Keep the menu open: saving to several playlists is one trip, not one per click.
      onSelect={(event) => event.preventDefault()}
      onCheckedChange={(checked) => onToggle(target.id, checked === true)}
    >
      <span className="min-w-0 flex-1 truncate">{target.name}</span>
      {isPrivate && (
        <Lock className="size-3 text-muted-foreground" aria-label="Private" />
      )}
    </DropdownMenuCheckboxItem>
  )
}
