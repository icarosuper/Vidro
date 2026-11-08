import { Link } from '@tanstack/react-router'
import { Home, LayoutDashboard, Settings, Tv, Upload } from 'lucide-react'
import { Button } from '#/components/ui/button'
import { useAuthModal, useIsAuthenticated } from '#/features/auth/hooks'
import { useCurrentUser } from '#/features/users/hooks'
import { AccountMenu } from './AccountMenu'
import { BrandLink } from './BrandLink'
import { SIDEBAR_ID, SidebarToggle } from './SidebarToggle'

const railLinkClass =
  'flex items-center gap-3 rounded-md px-3 py-2 text-sm font-semibold text-muted-foreground no-underline transition-colors hover:bg-accent hover:text-accent-foreground [&_svg]:size-4'
const railActiveProps = { className: 'bg-accent text-accent-foreground' }

function RailSectionTitle({ children }: { children: React.ReactNode }) {
  return (
    <p className="mt-8 mb-2 px-3 font-mono text-xs tracking-widest text-muted-foreground uppercase">
      {children}
    </p>
  )
}

function SignInPrompt() {
  const { openSignIn } = useAuthModal()
  return (
    <div className="mt-8 rounded-lg border border-border bg-background p-4">
      <p className="text-sm text-muted-foreground">
        Sign in to upload videos, follow channels and comment.
      </p>
      <Button size="sm" className="mt-3 w-full" onClick={openSignIn}>
        Sign in
      </Button>
    </div>
  )
}

function SignedInSections() {
  const { data: currentUser } = useCurrentUser()
  return (
    <>
      <RailSectionTitle>Creator</RailSectionTitle>
      <nav aria-label="Creator" className="flex flex-col gap-1">
        <Link
          to="/upload"
          className={railLinkClass}
          activeProps={railActiveProps}
        >
          <Upload /> Upload
        </Link>
        <Link
          to="/dashboard"
          className={railLinkClass}
          activeProps={railActiveProps}
        >
          <LayoutDashboard /> Dashboard
        </Link>
      </nav>
      <RailSectionTitle>You</RailSectionTitle>
      <nav aria-label="You" className="flex flex-col gap-1">
        {currentUser && (
          <Link
            to="/$username"
            params={{ username: currentUser.username }}
            className={railLinkClass}
            activeProps={railActiveProps}
          >
            <Tv /> Your channels
          </Link>
        )}
        <Link
          to="/settings"
          className={railLinkClass}
          activeProps={railActiveProps}
        >
          <Settings /> Settings
        </Link>
      </nav>
    </>
  )
}

// Desktop navigation (lg and up). Below lg the BottomNav takes over.
export function AppSidebar() {
  const isAuthenticated = useIsAuthenticated()
  return (
    <aside
      id={SIDEBAR_ID}
      aria-label="Sidebar"
      className="sticky top-0 hidden h-screen w-60 shrink-0 flex-col overflow-y-auto border-r border-border bg-secondary/60 px-4 pb-4 lg:flex lg:sidebar-collapsed:hidden"
    >
      {/* Same height and left inset as the top bar (h-14, px-4), so the hamburger stays put when
          the sidebar collapses and the top bar's one takes over. */}
      <div className="mb-4 flex h-14 shrink-0 items-center gap-1">
        <SidebarToggle />
        <BrandLink />
      </div>
      <nav aria-label="Main" className="flex flex-col gap-1">
        <Link
          to="/"
          className={railLinkClass}
          activeProps={railActiveProps}
          activeOptions={{ exact: true, includeSearch: false }}
        >
          <Home /> Home
        </Link>
      </nav>
      {isAuthenticated ? <SignedInSections /> : <SignInPrompt />}
      {isAuthenticated && (
        <div className="mt-auto pt-6">
          <AccountMenu layout="block" />
        </div>
      )}
    </aside>
  )
}
