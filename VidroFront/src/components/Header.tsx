import { Link, useNavigate } from '@tanstack/react-router'
import { LayoutDashboard, LogOut, Search, Upload, User } from 'lucide-react'
import ThemeToggle from '#/components/ThemeToggle'
import { Avatar, AvatarFallback, AvatarImage } from '#/components/ui/avatar'
import { Button } from '#/components/ui/button'
import { Input } from '#/components/ui/input'
import {
  useAuthModal,
  useIsAuthenticated,
  useSignOut,
} from '#/features/auth/hooks'
import { useCurrentUser } from '#/features/users/hooks'

function SearchBox() {
  const navigate = useNavigate()

  function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const typedQuery = String(
      new FormData(event.currentTarget).get('q') ?? '',
    ).trim()
    if (!typedQuery) return
    navigate({ to: '/search', search: { q: typedQuery } })
  }

  // Abaixo de sm a busca ganha a segunda linha inteira: disputando a primeira ela sobrava
  // com 17px de largura em 320px, o que é o mesmo que não ter busca.
  return (
    <search className="order-last mx-0 flex w-full min-w-0 basis-full sm:order-none sm:mx-4 sm:w-auto sm:max-w-md sm:basis-auto sm:flex-1">
      <form onSubmit={handleSubmit} className="flex w-full items-center gap-1">
        <Input
          name="q"
          type="search"
          placeholder="Search videos"
          aria-label="Search videos"
        />
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
      <div className="page-container flex min-h-14 flex-wrap items-center justify-between gap-1 py-2 sm:h-14 sm:flex-nowrap sm:py-0">
        <Link to="/" className="text-lg font-bold text-foreground no-underline">
          Vidro
        </Link>

        <SearchBox />

        {/* Até lg cada botão fica só com o ícone: o rótulo vira `sr-only`, não `hidden`,
            para o nome acessível continuar existindo no leitor de tela. Quatro rótulos de
            texto só cabem junto com a busca a partir de lg. */}
        <div className="flex shrink-0 items-center gap-0.5 sm:gap-1 lg:gap-2">
          <ThemeToggle />
          {isAuthenticated ? (
            <>
              <Link to="/upload">
                <Button variant="ghost" size="sm" className="gap-2">
                  <Upload className="h-4 w-4" />
                  <span className="sr-only lg:not-sr-only">Upload</span>
                </Button>
              </Link>
              <Link to="/dashboard">
                <Button variant="ghost" size="sm" className="gap-2">
                  <LayoutDashboard className="h-4 w-4" />
                  <span className="sr-only lg:not-sr-only">Dashboard</span>
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
                  <span className="sr-only lg:not-sr-only">Meu Perfil</span>
                </Button>
              </Link>
              <Button
                variant="ghost"
                size="sm"
                onClick={handleSignOut}
                disabled={signOut.isPending}
              >
                <LogOut className="h-4 w-4" />
                <span className="sr-only lg:not-sr-only lg:ml-1">Sign out</span>
              </Button>
            </>
          ) : (
            <Button size="sm" onClick={openSignIn}>
              Sign in
            </Button>
          )}
        </div>
      </div>
    </header>
  )
}
