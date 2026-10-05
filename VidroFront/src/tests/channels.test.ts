import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiClientError } from '#/shared/lib/api-client'
import { tokenStore } from '#/shared/lib/token-store'

const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

function mockResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

const channelFixture = {
  channelId: 'chan-1',
  handle: 'my-channel',
  name: 'My Channel',
  description: null,
  followerCount: 3,
  isFollowing: false,
  ownerId: 'user-1',
  ownerUsername: 'alice',
  avatarUrl: null,
  ownerAvatarUrl: null,
}

function fetchCall(index: number) {
  const call = mockFetch.mock.calls[index]
  if (!call) throw new Error(`expected fetch call #${index}`)
  const [url, options] = call
  return { url: url as string, options: options as RequestInit }
}

beforeEach(() => {
  tokenStore.set('test-token')
  mockFetch.mockReset()
})

afterEach(() => {
  tokenStore.clear()
  vi.restoreAllMocks()
})

describe('channels api', () => {
  it('getChannel reads the channel under its owner and unwraps the envelope', async () => {
    mockFetch.mockResolvedValueOnce(mockResponse({ data: channelFixture }))
    const { getChannel } = await import('#/features/channels/api')

    const channel = await getChannel('alice', 'my-channel')

    expect(channel).toEqual(channelFixture)
    const { url, options } = fetchCall(0)
    expect(url).toMatch(/\/v1\/users\/alice\/channels\/my-channel$/)
    expect(options.method).toBe('GET')
  })

  it('createChannel posts handle, name and description', async () => {
    mockFetch.mockResolvedValueOnce(
      mockResponse({ data: { channelId: 'chan-1', handle: 'my-channel' } }),
    )
    const { createChannel } = await import('#/features/channels/api')

    const created = await createChannel({
      handle: 'my-channel',
      name: 'My Channel',
      description: null,
    })

    expect(created.channelId).toBe('chan-1')
    const { url, options } = fetchCall(0)
    expect(url).toMatch(/\/v1\/channels$/)
    expect(options.method).toBe('POST')
    expect(JSON.parse(options.body as string)).toEqual({
      handle: 'my-channel',
      name: 'My Channel',
      description: null,
    })
  })

  it('updateChannel puts to the channel handle', async () => {
    mockFetch.mockResolvedValueOnce(new Response(null, { status: 204 }))
    const { updateChannel } = await import('#/features/channels/api')

    await updateChannel('my-channel', { name: 'Renamed', description: null })

    const { url, options } = fetchCall(0)
    expect(url).toMatch(/\/v1\/channels\/my-channel$/)
    expect(options.method).toBe('PUT')
    expect(JSON.parse(options.body as string)).toEqual({
      name: 'Renamed',
      description: null,
    })
  })

  it('follow and unfollow hit the same path with POST and DELETE', async () => {
    mockFetch.mockResolvedValue(new Response(null, { status: 204 }))
    const { followChannel, unfollowChannel } = await import(
      '#/features/channels/api'
    )

    await followChannel('alice', 'my-channel')
    await unfollowChannel('alice', 'my-channel')

    const follow = fetchCall(0)
    const unfollow = fetchCall(1)
    expect(follow.url).toMatch(
      /\/v1\/users\/alice\/channels\/my-channel\/follow$/,
    )
    expect(follow.options.method).toBe('POST')
    expect(unfollow.url).toBe(follow.url)
    expect(unfollow.options.method).toBe('DELETE')
  })

  it('surfaces the API error code, which SubscribeButton branches on', async () => {
    mockFetch.mockResolvedValueOnce(
      mockResponse(
        { code: 'channel.already_following', message: 'Already following' },
        409,
      ),
    )
    const { followChannel } = await import('#/features/channels/api')

    const followError = await followChannel('alice', 'my-channel').catch(
      (error: unknown) => error,
    )

    expect(followError).toBeInstanceOf(ApiClientError)
    expect((followError as ApiClientError).code).toBe(
      'channel.already_following',
    )
  })

  describe('uploadChannelAvatar', () => {
    const presignedUrl = 'https://minio.example.com/avatars/chan-1?sig=abc'

    function mockPresign() {
      mockFetch.mockResolvedValueOnce(
        mockResponse({
          data: {
            uploadUrl: presignedUrl,
            uploadExpiresAt: '2026-04-08T18:00:00Z',
          },
        }),
      )
    }

    it('asks the API for a presigned URL, then PUTs the file straight to storage', async () => {
      mockPresign()
      mockFetch.mockResolvedValueOnce(new Response(null, { status: 200 }))
      const { uploadChannelAvatar } = await import('#/features/channels/api')
      const file = new File(['img'], 'avatar.png', { type: 'image/png' })

      await uploadChannelAvatar('my-channel', file)

      expect(mockFetch).toHaveBeenCalledTimes(2)
      const presign = fetchCall(0)
      expect(presign.url).toMatch(/\/v1\/channels\/my-channel\/avatar$/)
      expect(presign.options.method).toBe('POST')

      const upload = fetchCall(1)
      expect(upload.url).toBe(presignedUrl)
      expect(upload.options.method).toBe('PUT')
      expect(upload.options.body).toBe(file)
      // The bearer token must not leak to storage; the signature in the URL is the auth.
      expect(upload.options.headers).toEqual({ 'Content-Type': 'image/png' })
    })

    it('rejects when storage refuses the upload', async () => {
      mockPresign()
      mockFetch.mockResolvedValueOnce(new Response(null, { status: 403 }))
      const { uploadChannelAvatar } = await import('#/features/channels/api')
      const file = new File(['img'], 'avatar.png', { type: 'image/png' })

      // An expired signature answers 403; resolving here would tell the owner the avatar
      // changed while the old one stays.
      await expect(uploadChannelAvatar('my-channel', file)).rejects.toThrow()
    })
  })
})
