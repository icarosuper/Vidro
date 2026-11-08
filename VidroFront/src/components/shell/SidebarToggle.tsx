import { Menu } from 'lucide-react'
import { Button } from '#/components/ui/button'
import { cn } from '#/lib/utils'
import { useLayoutPreference } from '#/shared/lib/layout-preferences'

export const SIDEBAR_ID = 'app-sidebar'

// Rendered twice: inside the sidebar (to collapse it) and in the top bar (to expand it again, shown
// only while collapsed). Only from lg up: below it there is no sidebar, the BottomNav takes over.
export function SidebarToggle({ className }: { className?: string }) {
  const [isCollapsed, toggleCollapsed] = useLayoutPreference('sidebarCollapsed')
  const label = isCollapsed ? 'Expand sidebar' : 'Collapse sidebar'

  return (
    <Button
      variant="ghost"
      size="icon"
      className={cn('hidden shrink-0 lg:inline-flex', className)}
      onClick={toggleCollapsed}
      aria-label={label}
      title={label}
      aria-expanded={!isCollapsed}
      aria-controls={SIDEBAR_ID}
    >
      <Menu className="size-5" />
    </Button>
  )
}
