import { Link } from '@tanstack/react-router'
import { Home, LayoutDashboard, Settings, Tv, Upload } from 'lucide-react'
import { useIsAuthenticated } from '#/features/auth/hooks'
import { useCurrentUser } from '#/features/users/hooks'

const tabClass =
  'flex flex-1 flex-col items-center gap-0.5 py-2 text-[11px] font-semibold text-muted-foreground no-underline [&_svg]:size-5'
const tabActiveProps = { className: 'text-primary' }

// Mobile navigation (below lg), within thumb reach. Signed out there is nothing to navigate to
// besides Home and the search, which the TopBar already has — so it only renders signed in.
export function BottomNav() {
  const isAuthenticated = useIsAuthenticated()
  const { data: currentUser } = useCurrentUser()
  if (!isAuthenticated) return null

  return (
    <>
      {/* Keeps the end of the page from hiding behind the fixed bar. */}
      <div aria-hidden className="h-20 lg:hidden" />
      <nav
        aria-label="Main"
        className="fixed inset-x-0 bottom-0 z-40 flex items-center border-t border-border bg-background/95 pb-[env(safe-area-inset-bottom)] backdrop-blur lg:hidden"
      >
        <Link
          to="/"
          className={tabClass}
          activeProps={tabActiveProps}
          activeOptions={{ exact: true, includeSearch: false }}
        >
          <Home /> Home
        </Link>
        {currentUser ? (
          <Link
            to="/$username"
            params={{ username: currentUser.username }}
            className={tabClass}
            activeProps={tabActiveProps}
          >
            <Tv /> Channels
          </Link>
        ) : (
          <span className={tabClass} />
        )}
        <Link
          to="/upload"
          aria-label="Upload"
          className="flex flex-1 justify-center"
        >
          <span className="grid size-11 place-items-center rounded-full bg-primary text-primary-foreground shadow-lg shadow-primary/30">
            <Upload className="size-5" />
          </span>
        </Link>
        <Link to="/dashboard" className={tabClass} activeProps={tabActiveProps}>
          <LayoutDashboard /> Dashboard
        </Link>
        <Link to="/settings" className={tabClass} activeProps={tabActiveProps}>
          <Settings /> Settings
        </Link>
      </nav>
    </>
  )
}
