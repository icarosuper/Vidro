// @vitest-environment jsdom

import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, renderHook, waitFor } from '@testing-library/react'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  playlistKeys,
  type SaveTarget,
  useSaveTargets,
  useToggleVideoInPlaylist,
} from '#/features/playlists/hooks'
import { tokenStore } from '#/shared/lib/token-store'

vi.mock('#/shared/lib/toast-error', () => ({ toastApiError: vi.fn() }))

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

function page(items: unknown[]) {
  return new Response(JSON.stringify({ data: { items, nextCursor: null } }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}

function summary(id: string, containsVideo: boolean) {
  return {
    id,
    name: id,
    description: null,
    visibility: { id: 0, value: 'Public' },
    videoCount: 0,
    createdAt: '2026-01-01T00:00:00Z',
    containsVideo,
  }
}

function createWrapper(queryClient: QueryClient) {
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  )
}

function urlOf(call: unknown[]) {
  return String(call[0])
}

beforeEach(() => {
  tokenStore.set('test-token')
  mockFetch.mockReset()
})

afterEach(() => {
  cleanup()
  tokenStore.clear()
})

describe('useSaveTargets', () => {
  it('asks both listings about the video and tags each group', async () => {
    mockFetch.mockImplementation((url: string) =>
      Promise.resolve(
        url.includes('/channels/')
          ? page([summary('channel-pl', true)])
          : page([summary('personal-pl', false)]),
      ),
    )
    const queryClient = new QueryClient()

    const { result } = renderHook(
      () =>
        useSaveTargets({
          videoId: 'vid-1',
          username: 'ana',
          ownChannelHandle: 'cooking',
        }),
      { wrapper: createWrapper(queryClient) },
    )

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    const urls = mockFetch.mock.calls.map(urlOf)
    expect(urls.every((url) => url.includes('videoId=vid-1'))).toBe(true)
    expect(result.current.data?.map((t) => [t.id, t.group])).toEqual([
      ['personal-pl', 'personal'],
      ['channel-pl', 'channel'],
    ])
  })

  it('skips the channel listing when the video is not the viewer’s', async () => {
    mockFetch.mockResolvedValue(page([]))
    const queryClient = new QueryClient()

    const { result } = renderHook(
      () =>
        useSaveTargets({
          videoId: 'vid-1',
          username: 'ana',
          ownChannelHandle: undefined,
        }),
      { wrapper: createWrapper(queryClient) },
    )

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(mockFetch).toHaveBeenCalledOnce()
    expect(urlOf(mockFetch.mock.calls[0] ?? [])).toContain(
      '/users/ana/playlists',
    )
  })
})

describe('useToggleVideoInPlaylist', () => {
  it('checks the box before the API answers', async () => {
    let answer: (response: Response) => void = () => {}
    mockFetch.mockReturnValue(
      new Promise<Response>((resolve) => {
        answer = resolve
      }),
    )
    const queryClient = new QueryClient()
    const targetsKey = [...playlistKeys.saveTargets('vid-1'), null]
    const target: SaveTarget = {
      ...summary('pl-1', false),
      group: 'personal',
    }
    queryClient.setQueryData<SaveTarget[]>(targetsKey, [target])

    const { result } = renderHook(() => useToggleVideoInPlaylist('vid-1'), {
      wrapper: createWrapper(queryClient),
    })
    result.current.mutate({ playlistId: 'pl-1', save: true })

    await waitFor(() =>
      expect(
        queryClient.getQueryData<SaveTarget[]>(targetsKey)?.[0]?.containsVideo,
      ).toBe(true),
    )
    expect(urlOf(mockFetch.mock.calls[0] ?? [])).toContain(
      '/v1/playlists/pl-1/items',
    )
    answer(new Response(null, { status: 204 }))
    await waitFor(() => expect(result.current.isSuccess).toBe(true))
  })
})
