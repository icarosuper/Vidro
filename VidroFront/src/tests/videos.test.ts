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

const videoSummaryFixture = {
  videoId: 'vid-1',
  channelId: 'chan-1',
  channelName: 'Test Channel',
  channelAvatarUrl: null,
  title: 'Test Video',
  description: 'A test video',
  tags: ['test'],
  viewCount: 100,
  likeCount: 10,
  thumbnailUrls: [],
  createdAt: '2024-01-01T00:00:00Z',
}

const videoFixture = {
  ...videoSummaryFixture,
  visibility: { id: 0, value: 'Public' },
  status: { id: 2, value: 'Ready' },
  dislikeCount: 2,
  commentCount: 5,
  videoUrl: 'https://cdn.example.com/video.m3u8',
}

beforeEach(() => {
  tokenStore.clear()
  mockFetch.mockReset()
})

afterEach(() => {
  vi.restoreAllMocks()
})

describe('videos api', () => {
  it('getTrending returns videos array', async () => {
    mockFetch.mockResolvedValueOnce(
      mockResponse({ data: { videos: [videoSummaryFixture] } }),
    )
    const { getTrending } = await import('#/features/videos/api')

    const result = await getTrending(20)

    expect(result.videos).toHaveLength(1)
    expect(result.videos[0].videoId).toBe('vid-1')
    expect(mockFetch).toHaveBeenCalledWith(
      expect.stringContaining('/v1/videos/trending?limit=20'),
      expect.objectContaining({ method: 'GET' }),
    )
  })

  it('getFeed returns videos and nextCursor', async () => {
    const nextCursor = '2024-01-01T00:00:00Z'
    mockFetch.mockResolvedValueOnce(
      mockResponse({ data: { videos: [videoSummaryFixture], nextCursor } }),
    )
    const { getFeed } = await import('#/features/videos/api')

    const result = await getFeed(20, undefined)

    expect(result.videos).toHaveLength(1)
    expect(result.nextCursor).toBe(nextCursor)
    expect(mockFetch).toHaveBeenCalledWith(
      expect.stringContaining('/v1/feed?limit=20'),
      expect.objectContaining({ method: 'GET' }),
    )
  })

  it('getFeed passes cursor as query param', async () => {
    mockFetch.mockResolvedValueOnce(
      mockResponse({ data: { videos: [], nextCursor: null } }),
    )
    const { getFeed } = await import('#/features/videos/api')
    const cursor = '2024-06-15T12:00:00Z'

    await getFeed(20, cursor)

    const [url] = mockFetch.mock.calls[0]
    expect(url).toContain('cursor=')
  })

  it('getVideo returns video detail', async () => {
    mockFetch.mockResolvedValueOnce(mockResponse({ data: videoFixture }))
    const { getVideo } = await import('#/features/videos/api')

    const result = await getVideo('vid-1')

    expect(result.videoId).toBe('vid-1')
    expect(result.videoUrl).toBe('https://cdn.example.com/video.m3u8')
    expect(mockFetch).toHaveBeenCalledWith(
      expect.stringContaining('/v1/videos/vid-1'),
      expect.objectContaining({ method: 'GET' }),
    )
  })

  it('registerView posts to view endpoint', async () => {
    mockFetch.mockResolvedValueOnce(new Response(null, { status: 204 }))
    const { registerView } = await import('#/features/videos/api')

    await registerView('vid-1')

    expect(mockFetch).toHaveBeenCalledWith(
      expect.stringContaining('/v1/videos/vid-1/view'),
      expect.objectContaining({ method: 'POST' }),
    )
  })

  it('reactToVideo posts reaction with type', async () => {
    tokenStore.set('test-token')
    mockFetch.mockResolvedValueOnce(new Response(null, { status: 204 }))
    const { reactToVideo } = await import('#/features/videos/api')

    await reactToVideo('vid-1', 1)

    const [url, options] = mockFetch.mock.calls[0]
    expect(url).toContain('/v1/videos/vid-1/react')
    expect(options.method).toBe('POST')
    expect(JSON.parse(options.body)).toEqual({ type: 1 })
  })

  it('removeReaction sends DELETE to react endpoint', async () => {
    tokenStore.set('test-token')
    mockFetch.mockResolvedValueOnce(new Response(null, { status: 204 }))
    const { removeReaction } = await import('#/features/videos/api')

    await removeReaction('vid-1')

    expect(mockFetch).toHaveBeenCalledWith(
      expect.stringContaining('/v1/videos/vid-1/react'),
      expect.objectContaining({ method: 'DELETE' }),
    )
  })
})
