// @vitest-environment jsdom

import { cleanup, render, screen } from '@testing-library/react'
import type { ReactNode } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  formatCompactAge,
  VideoCard,
  type VideoCardVideo,
} from '#/features/videos/components/VideoCard'

// Link needs a router context; a plain anchor keeps the accessible name and the target.
vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    params,
  }: {
    children: ReactNode
    to: string
    params?: Record<string, string>
  }) => {
    const href = Object.entries(params ?? {}).reduce(
      (path, [name, value]) => path.replace(`$${name}`, value),
      to,
    )
    return <a href={href}>{children}</a>
  },
}))

vi.mock('#/features/videos/hooks', () => ({
  useUploadThumbnail: () => ({ mutate: vi.fn(), isPending: false }),
  useUpdateVideo: () => ({ mutateAsync: vi.fn(), isPending: false }),
}))

const video: VideoCardVideo = {
  videoId: 'vid-1',
  thumbnailUrls: ['https://cdn/thumb-0.jpg'],
  title: 'Sintel — Official Trailer',
  channelName: 'Sintel',
  ownerUsername: 'durian',
  channelHandle: 'sintel',
  channelAvatarUrl: null,
  tags: ['animation', 'fantasy', 'trailer', 'blender'],
  viewCount: 1234,
  likeCount: 5,
  createdAt: new Date().toISOString(),
  visibility: { id: 1, value: 'Unlisted' },
  status: { id: 2, value: 'Ready' },
}

afterEach(() => {
  cleanup()
})

describe('VideoCard', () => {
  it('links the title to the watch page', () => {
    render(<VideoCard video={video} />)

    const titleLink = screen.getByRole('link', { name: video.title })
    expect(titleLink.getAttribute('href')).toBe('/watch/vid-1')
  })

  it('never nests the channel link inside the watch link', () => {
    render(<VideoCard video={video} />)

    const channelLink = screen.getByRole('link', { name: /Sintel$/ })
    expect(channelLink.getAttribute('href')).toBe('/durian/sintel')
    expect(channelLink.parentElement?.closest('a')).toBeNull()
  })

  it('reads the view count with its unit', () => {
    render(<VideoCard video={video} />)

    expect(screen.getByText(/1\.2K views/)).toBeTruthy()
  })

  it('shows at most three tags', () => {
    render(<VideoCard video={video} />)

    const tagList = screen.getByRole('list', { name: 'Tags' })
    expect(tagList.querySelectorAll('li')).toHaveLength(3)
  })

  it('hides the channel when the page is already the channel', () => {
    render(<VideoCard video={video} hideChannelInfo />)

    expect(screen.getAllByRole('link')).toHaveLength(1)
  })

  it('gives the owner the edit and thumbnail controls, and nobody else', () => {
    const { rerender } = render(<VideoCard video={video} />)
    expect(screen.queryByRole('button', { name: 'Edit video' })).toBeNull()

    rerender(<VideoCard video={video} isOwner />)
    expect(screen.getByRole('button', { name: 'Edit video' })).toBeTruthy()
    expect(
      screen.getByRole('button', { name: 'Upload thumbnail' }),
    ).toBeTruthy()
    expect(screen.getByText('Unlisted')).toBeTruthy()
  })
})

describe('formatCompactAge', () => {
  const now = new Date('2026-11-07T12:00:00Z').getTime()
  const minutesAgo = (minutes: number) =>
    new Date(now - minutes * 60_000).toISOString()

  it.each([
    [0, 'now'],
    [5, '5m'],
    [60 * 4, '4h'],
    [60 * 24 * 3, '3d'],
    [60 * 24 * 14, '2w'],
    [60 * 24 * 60, '2mo'],
    [60 * 24 * 800, '2y'],
  ])('%i minutes ago reads %s', (minutes, expected) => {
    expect(formatCompactAge(minutesAgo(minutes), now)).toBe(expected)
  })
})
