import { Link, useNavigate } from '@tanstack/react-router'
import { LogOut, Search, Upload, User } from 'lucide-react'
import { Button } from '#/components/ui/button'
import { Input } from '#/components/ui/input'
import { Avatar, AvatarFallback, AvatarImage } from '#/components/ui/avatar'
import { useAuthModal, useIsAuthenticated, useSignOut } from '#/features/auth/hooks'
import { useCurrentUser } from '#/features/users/hooks'

function SearchBox() {
  const navigate = useNavigate()

  function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const typedQuery = String(new FormData(event.currentTarget).get('q') ?? '').trim()
    if (!typedQuery) return
    navigate({ to: '/search', search: { q: typedQuery } })
  }

  return (
    <search className="mx-4 flex min-w-0 max-w-md flex-1">
      <form onSubmit={handleSubmit} className="flex w-full items-center gap-1">
        <Input name="q" type="search" placeholder="Search videos" aria-label="Search videos" />
        <Button type="submit" variant="ghost" size="sm" aria-label="Search">
          <Search className="h-4 w-4" />
        </Button>
      </form>
    </search>
  )
}

export function Header() {
  const isAuthenticated = useIsAuthenticated()
  const { openSignIn } = useAuthModal()
  const signOut = useSignOut()
  const { data: currentUser } = useCurrentUser()

  function handleSignOut() {
    signOut.mutate()
  }

  return (
    <header className="border-b border-border bg-background">
      <div className="page-container flex h-14 items-center justify-between">
        <Link to="/" className="text-lg font-bold text-foreground no-underline">
          Vidro
        </Link>

        <SearchBox />

        <div className="flex items-center gap-2">
          {isAuthenticated
            ? (
              <>
                <Link to="/upload">
                  <Button variant="ghost" size="sm" className="gap-2">
                    <Upload className="h-4 w-4" />
                    <span>Upload</span>
                  </Button>
                </Link>
                <Link to="/dashboard">
                  <Button variant="ghost" size="sm">
                    <span>Dashboard</span>
                  </Button>
                </Link>
                <Link to="/settings">
                  <Button variant="ghost" size="sm" className="gap-2">
                    <Avatar size="sm">
                      <AvatarImage src={currentUser?.avatarUrl ?? undefined} />
                      <AvatarFallback>
                        <User className="h-3 w-3" />
                      </AvatarFallback>
                    </Avatar>
                    <span>Meu Perfil</span>
                  </Button>
                </Link>
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={handleSignOut}
                  disabled={signOut.isPending}
                >
                  <LogOut className="h-4 w-4" />
                  <span className="ml-1">Sign out</span>
                </Button>
              </>
            )
            : (
              <Button size="sm" onClick={openSignIn}>
                Sign in
              </Button>
            )}
        </div>
      </div>
    </header>
  )
}
