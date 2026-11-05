import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { tokenStore } from '#/shared/lib/token-store'
import { API_ERROR_CODES } from '#/shared/types'

// Global fetch mock
const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

// Builds a mocked JSON response
function mockResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

beforeEach(() => {
  tokenStore.clear()
  mockFetch.mockReset()
})

afterEach(() => {
  vi.restoreAllMocks()
})

describe('apiClient', () => {
  it('sends a GET without a token when there is no session', async () => {
    const { apiClient } = await import('#/shared/lib/api-client')
    mockFetch.mockResolvedValueOnce(mockResponse({ data: { id: '1' } }))

    const result = await apiClient.get('/v1/test')

    expect(mockFetch).toHaveBeenCalledOnce()
    const [url, init] = mockFetch.mock.calls[0] as [string, RequestInit]
    expect(url).toContain('/v1/test')
    expect(
      (init.headers as Record<string, string>).Authorization,
    ).toBeUndefined()
    expect(result).toEqual({ id: '1' })
  })

  it('adds the Authorization header when there is a token', async () => {
    tokenStore.set('meu-access-token')
    const { apiClient } = await import('#/shared/lib/api-client')
    mockFetch.mockResolvedValueOnce(mockResponse({ data: { id: '1' } }))

    await apiClient.get('/v1/test')

    const [, init] = mockFetch.mock.calls[0] as [string, RequestInit]
    expect((init.headers as Record<string, string>).Authorization).toBe(
      'Bearer meu-access-token',
    )
  })

  it('throws a typed ApiClientError on an error response', async () => {
    const { apiClient, ApiClientError } = await import(
      '#/shared/lib/api-client'
    )
    mockFetch
      .mockResolvedValueOnce(
        mockResponse({ code: 'resource.not_found', message: 'Not found' }, 404),
      )
      .mockResolvedValueOnce(
        mockResponse({ code: 'resource.not_found', message: 'Not found' }, 404),
      )

    await expect(apiClient.get('/v1/missing')).rejects.toThrow(ApiClientError)
    await expect(apiClient.get('/v1/missing')).rejects.toMatchObject({
      code: 'resource.not_found',
      status: 404,
    })
  })

  it('tries renewing the token once on a 401', async () => {
    tokenStore.set('token-expirado')
    const { apiClient } = await import('#/shared/lib/api-client')

    const renewToken = vi.fn().mockResolvedValue('token-novo')
    apiClient.setRenewTokenCallback(renewToken)

    mockFetch
      .mockResolvedValueOnce(
        mockResponse(
          { code: 'request.unauthorized', message: 'Unauthorized' },
          401,
        ),
      )
      .mockResolvedValueOnce(mockResponse({ data: { id: '1' } }))

    const result = await apiClient.get('/v1/test')

    expect(renewToken).toHaveBeenCalledOnce()
    expect(tokenStore.get()).toBe('token-novo')
    expect(result).toEqual({ id: '1' })
  })

  it('throws and clears the token when the renewal fails', async () => {
    tokenStore.set('token-expirado')
    const { apiClient, ApiClientError } = await import(
      '#/shared/lib/api-client'
    )

    const renewToken = vi.fn().mockRejectedValue(new Error('refresh failed'))
    apiClient.setRenewTokenCallback(renewToken)

    mockFetch.mockResolvedValueOnce(
      mockResponse(
        { code: 'request.unauthorized', message: 'Unauthorized' },
        401,
      ),
    )

    await expect(apiClient.get('/v1/test')).rejects.toThrow(ApiClientError)
    expect(tokenStore.get()).toBeNull()
  })

  it('sends a POST with a JSON body', async () => {
    const { apiClient } = await import('#/shared/lib/api-client')
    mockFetch.mockResolvedValueOnce(
      mockResponse({ data: { videoId: 'abc' } }, 201),
    )

    const result = await apiClient.post(
      '/v1/users/joao/channels/meu-canal/videos',
      {
        title: 'Meu Vídeo',
        tags: [],
        visibility: 0,
      },
    )

    const [, init] = mockFetch.mock.calls[0] as [string, RequestInit]
    expect(init.method).toBe('POST')
    expect(JSON.parse(init.body as string)).toEqual({
      title: 'Meu Vídeo',
      tags: [],
      visibility: 0,
    })
    expect(result).toEqual({ videoId: 'abc' })
  })

  it('returns undefined on 204 responses', async () => {
    const { apiClient } = await import('#/shared/lib/api-client')
    mockFetch.mockResolvedValueOnce(new Response(null, { status: 204 }))

    const result = await apiClient.delete(
      '/v1/users/joao/channels/meu-canal/follow',
    )

    expect(result).toBeUndefined()
  })

  // The API's JWT middleware and rate limiter answer with no body; reading one as JSON used to
  // throw a SyntaxError, so the toast lost the error code.
  it('gives a bodiless 401 an unauthorized code when the renewal fails', async () => {
    tokenStore.set('expired-token')
    const { apiClient, ApiClientError } = await import(
      '#/shared/lib/api-client'
    )
    apiClient.setRenewTokenCallback(
      vi.fn().mockRejectedValue(new Error('refresh failed')),
    )
    mockFetch.mockResolvedValueOnce(new Response(null, { status: 401 }))

    const error = await apiClient.get('/v1/test').catch((e) => e)

    expect(error).toBeInstanceOf(ApiClientError)
    expect(error).toMatchObject({
      code: API_ERROR_CODES.UNAUTHORIZED,
      status: 401,
    })
  })

  it('gives a bodiless 429 a too-many-requests code', async () => {
    const { apiClient, ApiClientError } = await import(
      '#/shared/lib/api-client'
    )
    mockFetch.mockResolvedValueOnce(new Response(null, { status: 429 }))

    const error = await apiClient.post('/v1/auth/signin', {}).catch((e) => e)

    expect(error).toBeInstanceOf(ApiClientError)
    expect(error).toMatchObject({
      code: API_ERROR_CODES.TOO_MANY_REQUESTS,
      status: 429,
    })
  })

  it('gives an error body without a code a generic code', async () => {
    const { apiClient } = await import('#/shared/lib/api-client')
    mockFetch.mockResolvedValueOnce(
      mockResponse({ errors: [{ field: 'Title', message: 'Required' }] }, 400),
    )

    const error = await apiClient.post('/v1/test', {}).catch((e) => e)

    expect(error).toMatchObject({
      code: API_ERROR_CODES.REQUEST_FAILED,
      status: 400,
    })
  })
})

describe('correlation id', () => {
  it('sends a different X-Correlation-ID on every request', async () => {
    const { apiClient } = await import('#/shared/lib/api-client')
    // A Response can only be read once — each call needs its own.
    mockFetch.mockResolvedValueOnce(mockResponse({ data: { id: '1' } }))
    mockFetch.mockResolvedValueOnce(mockResponse({ data: { id: '2' } }))

    await apiClient.get('/v1/test')
    await apiClient.get('/v1/test')

    const sentIds = mockFetch.mock.calls.map(
      ([, init]) =>
        (init.headers as Record<string, string>)['X-Correlation-ID'],
    )
    expect(sentIds).toHaveLength(2)
    for (const id of sentIds) {
      expect(id).toBeTruthy()
    }
    expect(sentIds[0]).not.toBe(sentIds[1])
  })

  it('keeps the id that was sent on the error, for the user to quote to support', async () => {
    const { apiClient, ApiClientError } = await import(
      '#/shared/lib/api-client'
    )
    mockFetch.mockResolvedValueOnce(
      mockResponse({ code: 'NOT_FOUND', message: 'Video not found' }, 404),
    )

    const error = await apiClient.get('/v1/videos/inexistente').catch((e) => e)

    expect(error).toBeInstanceOf(ApiClientError)
    const [, init] = mockFetch.mock.calls[0] as [string, RequestInit]
    const sentId = (init.headers as Record<string, string>)['X-Correlation-ID']
    expect((error as InstanceType<typeof ApiClientError>).correlationId).toBe(
      sentId,
    )
  })
})
