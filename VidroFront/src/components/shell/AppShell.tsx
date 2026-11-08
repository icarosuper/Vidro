import { AppSidebar } from './AppSidebar'
import { BottomNav } from './BottomNav'
import { TopBar } from './TopBar'

// The frame around every route: sidebar from lg up, top bar with the search always, bottom tab
// bar below lg.
export function AppShell({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex min-h-screen">
      <AppSidebar />
      <div className="min-w-0 flex-1">
        <TopBar />
        {children}
        <BottomNav />
      </div>
    </div>
  )
}
