// @vitest-environment jsdom

import { cleanup, render, screen } from '@testing-library/react'
import type { ReactNode } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  findNextInPlaylist,
  PlaylistQueue,
} from '#/features/playlists/components/PlaylistQueue'
import type { Playlist } from '#/features/playlists/types'
import { PlaylistScope, PlaylistVisibility } from '#/shared/types'

// Renders the link's target into the href, so a test can see where it goes and with which list.
vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    params,
    search,
    ...rest
  }: {
    children: ReactNode
    params?: Record<string, string>
    search?: Record<string, string>
  }) => {
    const target = Object.values(params ?? {}).join('/')
    const query = search ? `?list=${search.list}` : ''
    return (
      <a href={`/${target}${query}`} {...rest}>
        {children}
      </a>
    )
  },
}))

afterEach(() => {
  cleanup()
})

function buildPlaylist(videoIds: string[]): Playlist {
  return {
    playlistId: 'pl-1',
    name: 'Favorites',
    description: null,
    visibility: { id: PlaylistVisibility.Public, value: 'Public' },
    scope: { id: PlaylistScope.User, value: 'User' },
    videoCount: videoIds.length,
    channelId: null,
    createdAt: '2026-01-01T00:00:00Z',
    isOwner: false,
    items: videoIds.map((videoId) => ({
      videoId,
      title: `Video ${videoId}`,
      thumbnailUrl: null,
      durationSeconds: null,
    })),
  }
}

describe('findNextInPlaylist', () => {
  const playlist = buildPlaylist(['a', 'b', 'c'])

  it('returns the video after the current one', () => {
    expect(findNextInPlaylist(playlist, 'a')?.videoId).toBe('b')
  })

  it('stops after the last one', () => {
    expect(findNextInPlaylist(playlist, 'c')).toBeNull()
  })

  it('does not jump into a playlist that does not hold the video', () => {
    expect(findNextInPlaylist(playlist, 'elsewhere')).toBeNull()
  })
})

describe('PlaylistQueue', () => {
  it('shows the position and marks the current video', () => {
    render(
      <PlaylistQueue
        playlist={buildPlaylist(['a', 'b', 'c'])}
        currentVideoId="b"
      />,
    )

    expect(screen.getByText(/2 \/ 3/)).toBeDefined()
    const current = screen.getByRole('link', { name: 'Video b' })
    expect(current.getAttribute('aria-current')).toBe('page')
    const other = screen.getByRole('link', { name: 'Video a' })
    expect(other.getAttribute('aria-current')).toBeNull()
  })

  it('keeps the playlist on every link, so the queue carries on', () => {
    render(
      <PlaylistQueue playlist={buildPlaylist(['a', 'b'])} currentVideoId="a" />,
    )

    expect(
      screen.getByRole('link', { name: 'Video b' }).getAttribute('href'),
    ).toBe('/b?list=pl-1')
  })
})
