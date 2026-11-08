import ThemeToggle from '#/components/ThemeToggle'
import { AccountMenu } from './AccountMenu'
import { BrandLink } from './BrandLink'
import { SearchBox } from './SearchBox'

export function TopBar() {
  return (
    <header className="sticky top-0 z-30 border-b border-border bg-background/90 backdrop-blur">
      <div className="page-container flex h-14 items-center gap-3">
        <BrandLink
          className="shrink-0 lg:hidden"
          wordmarkClassName="hidden sm:inline"
        />
        <SearchBox />
        <div className="ml-auto flex shrink-0 items-center gap-1">
          <ThemeToggle />
          {/* From lg up the account menu lives at the bottom of the sidebar. */}
          <div className="lg:hidden">
            <AccountMenu />
          </div>
        </div>
      </div>
    </header>
  )
}
