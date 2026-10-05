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
    expect(result.videos[0]?.videoId).toBe('vid-1')
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

    const [feedCall] = mockFetch.mock.calls
    if (!feedCall) throw new Error('getFeed did not call fetch')

    const [url] = feedCall
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

    const [reactCall] = mockFetch.mock.calls
    if (!reactCall) throw new Error('reactToVideo did not call fetch')

    const [url, options] = reactCall
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

  it('uploadThumbnail rejects when storage refuses the upload', async () => {
    mockFetch.mockResolvedValueOnce(
      mockResponse({
        data: {
          uploadUrl: 'https://minio.example.com/thumbnails/vid-1?sig=abc',
          uploadExpiresAt: '2026-04-08T18:00:00Z',
        },
      }),
    )
    mockFetch.mockResolvedValueOnce(new Response(null, { status: 403 }))
    const { uploadThumbnail } = await import('#/features/videos/api')
    const file = new File(['img'], 'thumb.png', { type: 'image/png' })

    // An expired signature answers 403; resolving here would report a thumbnail that
    // never reached storage.
    await expect(uploadThumbnail('vid-1', file)).rejects.toThrow()
  })
})

describe('uploadVideoFile', () => {
  class FakeXhr {
    status = 200
    upload = {}
    onload: (() => void) | null = null
    onerror: (() => void) | null = null
    onabort: (() => void) | null = null
    open = vi.fn()
    setRequestHeader = vi.fn()
    send = vi.fn()
    abort = vi.fn(() => this.onabort?.())
  }

  async function startUpload(signal: AbortSignal) {
    const xhr = new FakeXhr()
    vi.stubGlobal(
      'XMLHttpRequest',
      vi.fn(() => xhr),
    )
    const { uploadVideoFile } = await import('#/features/videos/api')
    const promise = uploadVideoFile(
      'https://minio/put',
      new File(['x'], 'v.mp4', { type: 'video/mp4' }),
      undefined,
      signal,
    )
    return { xhr, promise }
  }

  afterEach(() => {
    vi.unstubAllGlobals()
    vi.stubGlobal('fetch', mockFetch)
  })

  it('aborts the request when the signal fires', async () => {
    const controller = new AbortController()
    const { xhr } = await startUpload(controller.signal)

    controller.abort()

    expect(xhr.abort).toHaveBeenCalledOnce()
  })

  it('rejects without sending when the signal is already aborted', async () => {
    const controller = new AbortController()
    controller.abort()
    const { xhr, promise } = await startUpload(controller.signal)

    await expect(promise).rejects.toBe(controller.signal.reason)
    expect(xhr.send).not.toHaveBeenCalled()
  })

  it.each([
    ['success', (xhr: FakeXhr) => xhr.onload?.()],
    ['network error', (xhr: FakeXhr) => xhr.onerror?.()],
  ])('stops listening to the signal after %s', async (_, settle) => {
    const controller = new AbortController()
    const { xhr, promise } = await startUpload(controller.signal)

    settle(xhr)
    await promise.catch(() => {})
    controller.abort()

    expect(xhr.abort).not.toHaveBeenCalled()
  })
})
