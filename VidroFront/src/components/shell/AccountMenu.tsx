import { Link } from '@tanstack/react-router'
import {
  ChevronsUpDown,
  LayoutDashboard,
  LogOut,
  Settings,
  Tv,
  User,
} from 'lucide-react'
import { Avatar, AvatarFallback, AvatarImage } from '#/components/ui/avatar'
import { Button } from '#/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '#/components/ui/dropdown-menu'
import {
  useAuthModal,
  useIsAuthenticated,
  useSignOut,
} from '#/features/auth/hooks'
import { useCurrentUser } from '#/features/users/hooks'

type AccountMenuProps = {
  // `avatar`: a round avatar, for tight spots (the mobile top bar).
  // `block`: a full-width button with the name, for the bottom of the sidebar.
  layout?: 'avatar' | 'block'
}

export function AccountMenu({ layout = 'avatar' }: AccountMenuProps) {
  const isAuthenticated = useIsAuthenticated()
  const { openSignIn } = useAuthModal()
  const signOut = useSignOut()
  const { data: currentUser } = useCurrentUser()

  if (!isAuthenticated) {
    return (
      <Button size="sm" onClick={openSignIn}>
        Sign in
      </Button>
    )
  }

  const isBlock = layout === 'block'
  const avatar = (
    <Avatar>
      <AvatarImage src={currentUser?.avatarUrl ?? undefined} />
      <AvatarFallback>
        <User className="size-4" />
      </AvatarFallback>
    </Avatar>
  )

  return (
    <DropdownMenu>
      {isBlock ? (
        <DropdownMenuTrigger
          aria-label="Account menu"
          className="flex w-full items-center gap-3 rounded-md border border-border bg-background px-3 py-2.5 text-left outline-none transition-colors hover:bg-accent focus-visible:ring-2 focus-visible:ring-ring data-[state=open]:bg-accent"
        >
          {avatar}
          <span className="min-w-0 flex-1">
            <span className="block truncate text-sm font-bold">
              {currentUser?.username ?? 'Account'}
            </span>
            <span className="block font-mono text-xs text-muted-foreground">
              Account
            </span>
          </span>
          <ChevronsUpDown className="size-4 shrink-0 text-muted-foreground" />
        </DropdownMenuTrigger>
      ) : (
        <DropdownMenuTrigger
          aria-label="Account menu"
          className="rounded-full outline-none focus-visible:ring-2 focus-visible:ring-ring"
        >
          {avatar}
        </DropdownMenuTrigger>
      )}
      <DropdownMenuContent
        side={isBlock ? 'top' : 'bottom'}
        align={isBlock ? 'start' : 'end'}
        className={
          isBlock ? 'w-(--radix-dropdown-menu-trigger-width)' : 'min-w-52'
        }
      >
        {currentUser && (
          <>
            <DropdownMenuLabel className="font-mono text-xs text-muted-foreground">
              @{currentUser.username}
            </DropdownMenuLabel>
            <DropdownMenuItem asChild>
              <Link to="/$username" params={{ username: currentUser.username }}>
                <Tv /> Your channels
              </Link>
            </DropdownMenuItem>
          </>
        )}
        <DropdownMenuItem asChild>
          <Link to="/dashboard">
            <LayoutDashboard /> Dashboard
          </Link>
        </DropdownMenuItem>
        <DropdownMenuItem asChild>
          <Link to="/settings">
            <Settings /> Settings
          </Link>
        </DropdownMenuItem>
        <DropdownMenuSeparator />
        <DropdownMenuItem
          onSelect={() => signOut.mutate()}
          disabled={signOut.isPending}
        >
          <LogOut /> Sign out
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
