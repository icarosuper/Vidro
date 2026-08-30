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

const profileFixture = {
  userId: 'user-1',
  username: 'testuser',
  email: 'test@example.com',
  avatarUrl: null,
}

beforeEach(() => {
  tokenStore.set('test-token')
  mockFetch.mockReset()
})

afterEach(() => {
  tokenStore.clear()
  vi.restoreAllMocks()
})

describe('users api', () => {
  it('getMe returns the current user profile', async () => {
    mockFetch.mockResolvedValueOnce(mockResponse({ data: profileFixture }))
    const { getMe } = await import('#/features/users/api')

    const result = await getMe()

    expect(result).toEqual(profileFixture)
    expect(mockFetch).toHaveBeenCalledWith(
      expect.stringContaining('/v1/users/me'),
      expect.objectContaining({ method: 'GET' }),
    )
  })

  it('uploadAvatar posts to get presigned URL then PUTs file to MinIO', async () => {
    const presignedUrl = 'https://minio.example.com/avatars/user-1?sig=abc'

    // First call: POST /v1/users/me/avatar → presigned URL
    mockFetch.mockResolvedValueOnce(
      mockResponse({ data: { uploadUrl: presignedUrl, uploadExpiresAt: '2026-04-08T18:00:00Z' } }),
    )
    // Second call: PUT to MinIO presigned URL
    mockFetch.mockResolvedValueOnce(new Response(null, { status: 200 }))

    const { uploadAvatar } = await import('#/features/users/api')

    const file = new File(['img'], 'avatar.jpg', { type: 'image/jpeg' })
    await uploadAvatar(file)

    expect(mockFetch).toHaveBeenCalledTimes(2)

    const [firstUrl, firstOptions] = mockFetch.mock.calls[0]
    expect(firstUrl).toContain('/v1/users/me/avatar')
    expect(firstOptions.method).toBe('POST')

    const [secondUrl, secondOptions] = mockFetch.mock.calls[1]
    expect(secondUrl).toBe(presignedUrl)
    expect(secondOptions.method).toBe('PUT')
    expect(secondOptions.body).toBe(file)
  })
})
