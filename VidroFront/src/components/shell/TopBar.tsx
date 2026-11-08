import ThemeToggle from '#/components/ThemeToggle'
import { AccountMenu } from './AccountMenu'
import { BrandLink } from './BrandLink'
import { SearchBox } from './SearchBox'
import { SidebarToggle } from './SidebarToggle'

export function TopBar() {
  return (
    <header className="sticky top-0 z-30 border-b border-border bg-background/90 backdrop-blur">
      <div className="flex h-14 items-center gap-3 px-6 lg:px-4">
        <SidebarToggle className="lg:hidden lg:sidebar-collapsed:inline-flex" />
        {/* The sidebar carries the brand and the account menu; with it collapsed (or gone, below
            lg) the top bar shows them instead. */}
        <BrandLink
          className="shrink-0 lg:hidden lg:sidebar-collapsed:flex"
          wordmarkClassName="hidden sm:inline"
        />
        <SearchBox />
        <div className="ml-auto flex shrink-0 items-center gap-1">
          <ThemeToggle />
          <div className="lg:hidden lg:sidebar-collapsed:block">
            <AccountMenu />
          </div>
        </div>
      </div>
    </header>
  )
}
