// @vitest-environment jsdom

import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AppShell } from '#/components/shell/AppShell'

const navigate = vi.fn()

// Link needs a router context; a plain anchor keeps the accessible name and the target.
vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    params,
    'aria-label': ariaLabel,
  }: {
    children: ReactNode
    to: string
    params?: { username?: string }
    'aria-label'?: string
  }) => (
    <a
      href={params?.username ? to.replace('$username', params.username) : to}
      aria-label={ariaLabel}
    >
      {children}
    </a>
  ),
  useNavigate: () => navigate,
}))

// Not under test here, and it needs localStorage, which this jsdom setup does not provide.
vi.mock('#/components/ThemeToggle', () => ({ default: () => null }))

let isAuthenticated = false
const openSignIn = vi.fn()

vi.mock('#/features/auth/hooks', () => ({
  useIsAuthenticated: () => isAuthenticated,
  useAuthModal: () => ({ openSignIn }),
  useSignOut: () => ({ mutate: vi.fn(), isPending: false }),
}))

vi.mock('#/features/users/hooks', () => ({
  useCurrentUser: () => ({
    data: isAuthenticated
      ? { userId: 'u1', username: 'priya', email: 'p@x.com', avatarUrl: null }
      : undefined,
  }),
}))

afterEach(() => {
  cleanup()
  vi.clearAllMocks()
  isAuthenticated = false
})

describe('AppShell signed out', () => {
  it('offers sign in and no creator navigation', () => {
    render(<AppShell>page</AppShell>)

    const signInButtons = screen.getAllByRole('button', { name: 'Sign in' })
    expect(signInButtons.length).toBeGreaterThan(0)
    expect(screen.queryByRole('link', { name: 'Upload' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Account menu' })).toBeNull()
  })
})

describe('AppShell signed in', () => {
  beforeEach(() => {
    isAuthenticated = true
  })

  it('shows the creator links in both the sidebar and the bottom tab bar', () => {
    render(<AppShell>page</AppShell>)

    const uploadLinks = screen.getAllByRole('link', { name: 'Upload' })
    expect(uploadLinks).toHaveLength(2)
    expect(screen.getAllByRole('button', { name: 'Account menu' }).length).toBe(
      2,
    )
  })

  it("links to the current user's channels", () => {
    render(<AppShell>page</AppShell>)

    const channelsLink = screen.getByRole('link', { name: /Your channels/ })
    expect(channelsLink.getAttribute('href')).toBe('/priya')
  })
})

describe('SidebarToggle', () => {
  afterEach(() => {
    document.documentElement.removeAttribute('data-sidebar-collapsed')
  })

  it('collapses the sidebar from inside it and keeps both toggles in step', () => {
    render(<AppShell>page</AppShell>)

    // One inside the sidebar, one in the top bar (CSS shows only one of them at a time).
    const toggles = screen.getAllByRole('button', { name: 'Collapse sidebar' })
    expect(toggles).toHaveLength(2)
    const sidebar = document.getElementById('app-sidebar')
    const sidebarToggle = toggles.find((toggle) => sidebar?.contains(toggle))
    if (!sidebarToggle) throw new Error('no toggle inside the sidebar')
    expect(sidebarToggle.getAttribute('aria-controls')).toBe('app-sidebar')
    expect(sidebarToggle.getAttribute('aria-expanded')).toBe('true')

    fireEvent.click(sidebarToggle)

    expect(
      document.documentElement.hasAttribute('data-sidebar-collapsed'),
    ).toBe(true)
    const expandToggles = screen.getAllByRole('button', {
      name: 'Expand sidebar',
    })
    expect(expandToggles).toHaveLength(2)
    for (const toggle of expandToggles) {
      expect(toggle.getAttribute('aria-expanded')).toBe('false')
    }
  })
})

describe('SearchBox', () => {
  it('navigates to the search page with the trimmed query', () => {
    render(<AppShell>page</AppShell>)

    const searchInput = screen.getByRole('searchbox', { name: 'Search videos' })
    fireEvent.change(searchInput, { target: { value: '  blender ' } })
    fireEvent.submit(searchInput)

    expect(navigate).toHaveBeenCalledWith({
      to: '/search',
      search: { q: 'blender' },
    })
  })

  it('ignores an empty query', () => {
    render(<AppShell>page</AppShell>)

    fireEvent.submit(screen.getByRole('searchbox', { name: 'Search videos' }))

    expect(navigate).not.toHaveBeenCalled()
  })

  it('focuses the search when "/" is pressed outside a field', () => {
    render(<AppShell>page</AppShell>)

    fireEvent.keyDown(window, { key: '/' })

    const searchInput = screen.getByRole('searchbox', { name: 'Search videos' })
    expect(document.activeElement).toBe(searchInput)
  })
})
