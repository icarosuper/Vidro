import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { tokenStore } from '#/shared/lib/token-store'

// Mock do fetch global
const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

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

describe('tokenStore', () => {
  it('starts null', () => {
    expect(tokenStore.get()).toBeNull()
  })

  it('stores and retrieves a token', () => {
    tokenStore.set('abc123')
    expect(tokenStore.get()).toBe('abc123')
  })

  it('clears the token', () => {
    tokenStore.set('abc123')
    tokenStore.clear()
    expect(tokenStore.get()).toBeNull()
  })

  it('notifies subscribers on set', () => {
    const listener = vi.fn()
    const unsubscribe = tokenStore.subscribe(listener)

    tokenStore.set('new-token')
    expect(listener).toHaveBeenCalledOnce()

    unsubscribe()
    tokenStore.set('another')
    expect(listener).toHaveBeenCalledOnce() // not called again after unsubscribe
  })

  it('notifies subscribers on clear', () => {
    tokenStore.set('token')
    const listener = vi.fn()
    tokenStore.subscribe(listener)

    tokenStore.clear()
    expect(listener).toHaveBeenCalledOnce()
  })
})

describe('apiClient with renewed token', () => {
  it('tenta renovar o token uma vez em caso de 401', async () => {
    tokenStore.set('token-expirado')
    const { apiClient } = await import('#/shared/lib/api-client')

    const renewToken = vi.fn().mockResolvedValue('token-novo')
    apiClient.setRenewTokenCallback(renewToken)

    mockFetch
      .mockResolvedValueOnce(mockResponse({ code: 'request.unauthorized', message: 'Unauthorized' }, 401))
      .mockResolvedValueOnce(mockResponse({ data: { id: '1' } }))

    const result = await apiClient.get('/v1/test')

    expect(renewToken).toHaveBeenCalledOnce()
    expect(tokenStore.get()).toBe('token-novo')
    expect(result).toEqual({ id: '1' })

    // cleanup
    apiClient.setRenewTokenCallback(null)
  })

  it('limpa o token quando a renovação falha', async () => {
    tokenStore.set('token-expirado')
    const { apiClient, ApiClientError } = await import('#/shared/lib/api-client')

    const renewToken = vi.fn().mockRejectedValue(new Error('refresh failed'))
    apiClient.setRenewTokenCallback(renewToken)

    mockFetch.mockResolvedValueOnce(
      mockResponse({ code: 'request.unauthorized', message: 'Unauthorized' }, 401),
    )

    await expect(apiClient.get('/v1/test')).rejects.toThrow(ApiClientError)
    expect(tokenStore.get()).toBeNull()

    // cleanup
    apiClient.setRenewTokenCallback(null)
  })
})
