// @vitest-environment jsdom

import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { SaveToPlaylistButton } from '#/features/playlists/components/SaveToPlaylistButton'
import type { SaveTarget } from '#/features/playlists/hooks'
import { PlaylistVisibility } from '#/shared/types'

const toggleMutate = vi.fn()
const openSignIn = vi.fn()
let isAuthenticated = true
let targets: SaveTarget[] = []

vi.mock('#/features/auth/hooks', () => ({
  useIsAuthenticated: () => isAuthenticated,
  useAuthModal: () => ({ openSignIn }),
}))

vi.mock('#/features/users/hooks', () => ({
  useCurrentUser: () => ({ data: { username: 'ana' } }),
}))

vi.mock('#/features/playlists/hooks', () => ({
  useSaveTargets: () => ({
    data: targets,
    isPending: false,
    isError: false,
    refetch: vi.fn(),
  }),
  useToggleVideoInPlaylist: () => ({ mutate: toggleMutate }),
  useCreatePlaylist: () => ({ mutate: vi.fn(), isPending: false, error: null }),
}))

function buildTarget(overrides: Partial<SaveTarget> = {}): SaveTarget {
  return {
    id: 'pl-1',
    name: 'Favorites',
    description: null,
    visibility: { id: PlaylistVisibility.Public, value: 'Public' },
    videoCount: 0,
    createdAt: '2026-01-01T00:00:00Z',
    containsVideo: false,
    group: 'personal',
    ...overrides,
  }
}

// Radix opens the menu from the keyboard as well as the pointer; jsdom has no PointerEvent.
function openMenu(name: string) {
  fireEvent.keyDown(screen.getByRole('button', { name }), { key: 'Enter' })
}

beforeEach(() => {
  isAuthenticated = true
  targets = []
})

afterEach(() => {
  cleanup()
  vi.resetAllMocks()
})

describe('SaveToPlaylistButton', () => {
  it('asks a signed-out viewer to sign in instead of opening the menu', () => {
    isAuthenticated = false
    render(<SaveToPlaylistButton videoId="vid-1" />)

    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(openSignIn).toHaveBeenCalledOnce()
    expect(screen.queryByRole('menu')).toBeNull()
  })

  it('reads "Saved" once the video is in any playlist', () => {
    targets = [buildTarget({ containsVideo: true })]
    render(<SaveToPlaylistButton videoId="vid-1" />)

    expect(screen.getByRole('button', { name: 'Saved' })).toBeDefined()
  })

  it('checks the playlists that hold the video, and toggles both ways', () => {
    targets = [
      buildTarget({ id: 'with', name: 'With it', containsVideo: true }),
      buildTarget({ id: 'without', name: 'Without it' }),
    ]
    render(<SaveToPlaylistButton videoId="vid-1" />)
    openMenu('Saved')

    const withIt = screen.getByRole('menuitemcheckbox', { name: 'With it' })
    const withoutIt = screen.getByRole('menuitemcheckbox', {
      name: 'Without it',
    })
    expect(withIt.getAttribute('aria-checked')).toBe('true')
    expect(withoutIt.getAttribute('aria-checked')).toBe('false')

    fireEvent.click(withoutIt)
    expect(toggleMutate).toHaveBeenCalledWith({
      playlistId: 'without',
      save: true,
    })
    fireEvent.click(withIt)
    expect(toggleMutate).toHaveBeenCalledWith({
      playlistId: 'with',
      save: false,
    })
  })

  it('labels the groups only when channel playlists are offered', () => {
    targets = [
      buildTarget({ id: 'mine', name: 'Mine' }),
      buildTarget({ id: 'chan', name: 'Channel one', group: 'channel' }),
    ]
    render(<SaveToPlaylistButton videoId="vid-1" ownChannelHandle="ch" />)
    openMenu('Save')

    expect(screen.getByText('Your playlists')).toBeDefined()
    expect(screen.getByText('This channel')).toBeDefined()
  })

  it('says there are no playlists yet and still offers to create one', () => {
    render(<SaveToPlaylistButton videoId="vid-1" />)
    openMenu('Save')

    expect(screen.getByText('No playlists yet.')).toBeDefined()
    expect(screen.getByRole('menuitem', { name: 'New playlist' })).toBeDefined()
  })
})
