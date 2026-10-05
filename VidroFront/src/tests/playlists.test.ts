import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { tokenStore } from '#/shared/lib/token-store'

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

function mockResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

function fetchCall(index: number) {
  const call = mockFetch.mock.calls[index]
  if (!call) throw new Error(`expected fetch call #${index}`)
  const [url, options] = call
  return { url: url as string, options: options as RequestInit }
}

const emptyPage = { items: [], nextCursor: null }

beforeEach(() => {
  tokenStore.set('test-token')
  mockFetch.mockReset()
})

afterEach(() => {
  tokenStore.clear()
  vi.restoreAllMocks()
})

describe('playlists api', () => {
  it('getPlaylist unwraps the envelope', async () => {
    const playlistFixture = {
      playlistId: 'pl-1',
      name: 'Favorites',
      description: null,
      visibility: { id: 0, value: 'Public' },
      scope: { id: 0, value: 'User' },
      videoCount: 0,
      channelId: null,
      createdAt: '2024-01-01T00:00:00Z',
      items: [],
    }
    mockFetch.mockResolvedValueOnce(mockResponse({ data: playlistFixture }))
    const { getPlaylist } = await import('#/features/playlists/api')

    const playlist = await getPlaylist('pl-1')

    expect(playlist).toEqual(playlistFixture)
    expect(fetchCall(0).url).toMatch(/\/v1\/playlists\/pl-1$/)
  })

  it('listChannelPlaylists sends the limit and omits an absent cursor', async () => {
    mockFetch.mockResolvedValueOnce(mockResponse({ data: emptyPage }))
    const { listChannelPlaylists } = await import('#/features/playlists/api')

    await listChannelPlaylists('alice', 'my-channel', 20, undefined)

    const url = new URL(fetchCall(0).url)
    expect(url.pathname).toBe('/v1/users/alice/channels/my-channel/playlists')
    expect(url.searchParams.get('limit')).toBe('20')
    expect(url.searchParams.has('cursor')).toBe(false)
  })

  it('listUserPlaylists passes the cursor through for the next page', async () => {
    mockFetch.mockResolvedValueOnce(mockResponse({ data: emptyPage }))
    const { listUserPlaylists } = await import('#/features/playlists/api')
    const cursor = '2024-06-15T12:00:00Z'

    await listUserPlaylists('alice', 20, cursor)

    const url = new URL(fetchCall(0).url)
    expect(url.pathname).toBe('/v1/users/alice/playlists')
    expect(url.searchParams.get('cursor')).toBe(cursor)
  })

  it('createPlaylist posts the full request and returns the new id', async () => {
    mockFetch.mockResolvedValueOnce(
      mockResponse({ data: { playlistId: 'pl-1' } }),
    )
    const { createPlaylist } = await import('#/features/playlists/api')
    const request = {
      name: 'Favorites',
      description: null,
      visibility: 1,
      scope: 1,
      channelId: 'chan-1',
    }

    const created = await createPlaylist(request)

    expect(created.playlistId).toBe('pl-1')
    const { url, options } = fetchCall(0)
    expect(url).toMatch(/\/v1\/playlists$/)
    expect(options.method).toBe('POST')
    expect(JSON.parse(options.body as string)).toEqual(request)
  })

  it('addVideoToPlaylist posts the video id in the body', async () => {
    mockFetch.mockResolvedValueOnce(new Response(null, { status: 204 }))
    const { addVideoToPlaylist } = await import('#/features/playlists/api')

    await addVideoToPlaylist('pl-1', 'vid-1')

    const { url, options } = fetchCall(0)
    expect(url).toMatch(/\/v1\/playlists\/pl-1\/items$/)
    expect(options.method).toBe('POST')
    expect(JSON.parse(options.body as string)).toEqual({ videoId: 'vid-1' })
  })

  it('removeVideoFromPlaylist deletes the item by video id', async () => {
    mockFetch.mockResolvedValueOnce(new Response(null, { status: 204 }))
    const { removeVideoFromPlaylist } = await import('#/features/playlists/api')

    await removeVideoFromPlaylist('pl-1', 'vid-1')

    const { url, options } = fetchCall(0)
    expect(url).toMatch(/\/v1\/playlists\/pl-1\/items\/vid-1$/)
    expect(options.method).toBe('DELETE')
  })

  it('deletePlaylist deletes the playlist', async () => {
    mockFetch.mockResolvedValueOnce(new Response(null, { status: 204 }))
    const { deletePlaylist } = await import('#/features/playlists/api')

    await deletePlaylist('pl-1')

    const { url, options } = fetchCall(0)
    expect(url).toMatch(/\/v1\/playlists\/pl-1$/)
    expect(options.method).toBe('DELETE')
  })
})
