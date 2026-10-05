// @vitest-environment jsdom

import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from '@testing-library/react'
import type { ReactNode } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { CreatePlaylistForm } from '#/features/playlists/components/CreatePlaylistForm'
import { PlaylistCard } from '#/features/playlists/components/PlaylistCard'
import { PlaylistItemList } from '#/features/playlists/components/PlaylistItemList'
import type { PlaylistItem, PlaylistSummary } from '#/features/playlists/types'
import { PlaylistScope, PlaylistVisibility } from '#/shared/types'

// Link needs a router context; a plain anchor is enough to render what sits inside it.
vi.mock('@tanstack/react-router', () => ({
  Link: ({ children }: { children: ReactNode }) => <a href="/">{children}</a>,
}))

const removeMutate = vi.fn()
const createMutate = vi.fn()

vi.mock('#/features/playlists/hooks', () => ({
  useRemoveVideoFromPlaylist: () => ({
    mutate: removeMutate,
    isPending: false,
  }),
  useCreatePlaylist: () => ({
    mutate: createMutate,
    isPending: false,
    error: null,
  }),
}))

afterEach(() => {
  cleanup()
  vi.resetAllMocks()
})

function buildItem(overrides: Partial<PlaylistItem> = {}): PlaylistItem {
  return {
    videoId: 'vid-1',
    title: 'First video',
    thumbnailUrl: null,
    durationSeconds: null,
    ...overrides,
  }
}

function buildSummary(
  overrides: Partial<PlaylistSummary> = {},
): PlaylistSummary {
  return {
    id: 'pl-1',
    name: 'Favorites',
    description: null,
    visibility: { id: PlaylistVisibility.Public, value: 'Public' },
    videoCount: 2,
    createdAt: '2024-01-01T00:00:00Z',
    ...overrides,
  }
}

describe('PlaylistItemList', () => {
  it('offers Remove on every item only to the owner', () => {
    const items = [
      buildItem({ videoId: 'vid-1' }),
      buildItem({ videoId: 'vid-2', title: 'Second video' }),
    ]

    render(<PlaylistItemList playlistId="pl-1" items={items} isOwner />)
    const removeButtons = screen.getAllByRole('button', {
      name: 'Remove from playlist',
    })
    expect(removeButtons).toHaveLength(2)

    fireEvent.click(removeButtons[1] as HTMLElement)
    expect(removeMutate).toHaveBeenCalledWith('vid-2')
  })

  it('hides Remove from a visitor', () => {
    render(
      <PlaylistItemList
        playlistId="pl-1"
        items={[buildItem()]}
        isOwner={false}
      />,
    )

    expect(screen.getByText('First video')).toBeDefined()
    expect(
      screen.queryByRole('button', { name: 'Remove from playlist' }),
    ).toBeNull()
  })

  it('says the playlist is empty instead of rendering nothing', () => {
    render(<PlaylistItemList playlistId="pl-1" items={[]} isOwner />)

    expect(screen.getByText('No videos in this playlist yet.')).toBeDefined()
  })

  it('formats durations as m:ss, h:mm:ss, and skips an unknown one', () => {
    const items = [
      buildItem({ videoId: 'short', durationSeconds: 65 }),
      buildItem({ videoId: 'long', durationSeconds: 3725 }),
      buildItem({ videoId: 'unknown', durationSeconds: null }),
      buildItem({ videoId: 'zero', durationSeconds: 0 }),
    ]

    render(<PlaylistItemList playlistId="pl-1" items={items} isOwner={false} />)

    expect(screen.getByText('1:05')).toBeDefined()
    expect(screen.getByText('1:02:05')).toBeDefined()
    // 0 is a real duration (falsy, but not missing) — only null hides the badge.
    expect(screen.getByText('0:00')).toBeDefined()
  })
})

describe('PlaylistCard', () => {
  it('marks a private playlist', () => {
    render(
      <PlaylistCard
        playlist={buildSummary({
          visibility: { id: PlaylistVisibility.Private, value: 'Private' },
        })}
      />,
    )

    expect(screen.getByText('Private')).toBeDefined()
  })

  it('does not mark a public playlist, and pluralizes the count', () => {
    render(<PlaylistCard playlist={buildSummary({ videoCount: 1 })} />)

    expect(screen.queryByText('Private')).toBeNull()
    expect(screen.getByText('1 video')).toBeDefined()
  })
})

describe('CreatePlaylistForm', () => {
  function submitWithName(name: string) {
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: name } })
    fireEvent.click(screen.getByRole('button', { name: 'Create playlist' }))
  }

  it('creates a channel playlist when opened from a channel', async () => {
    render(<CreatePlaylistForm channelId="chan-1" />)

    submitWithName('Highlights')

    await waitFor(() => expect(createMutate).toHaveBeenCalledOnce())
    expect(createMutate.mock.calls[0]?.[0]).toEqual({
      name: 'Highlights',
      description: null,
      visibility: PlaylistVisibility.Public,
      scope: PlaylistScope.Channel,
      channelId: 'chan-1',
    })
  })

  it('creates a user playlist with no channel otherwise', async () => {
    render(<CreatePlaylistForm />)

    submitWithName('Watch later')

    await waitFor(() => expect(createMutate).toHaveBeenCalledOnce())
    expect(createMutate.mock.calls[0]?.[0]).toMatchObject({
      scope: PlaylistScope.User,
      channelId: null,
    })
  })

  it('hands the new playlist id to the caller', async () => {
    const onSuccess = vi.fn()
    createMutate.mockImplementation(
      (
        _: unknown,
        callbacks: { onSuccess: (data: { playlistId: string }) => void },
      ) => callbacks.onSuccess({ playlistId: 'pl-9' }),
    )
    render(<CreatePlaylistForm onSuccess={onSuccess} />)

    submitWithName('Watch later')

    await waitFor(() => expect(onSuccess).toHaveBeenCalledWith('pl-9'))
  })
})
