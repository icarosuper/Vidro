import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { tokenStore } from '#/shared/lib/token-store'

// Mock do fetch global
const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

// Helper para criar respostas mock
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
  it('faz GET sem token quando não há sessão', async () => {
    const { apiClient } = await import('#/shared/lib/api-client')
    mockFetch.mockResolvedValueOnce(mockResponse({ data: { id: '1' } }))

    const result = await apiClient.get('/v1/test')

    expect(mockFetch).toHaveBeenCalledOnce()
    const [url, init] = mockFetch.mock.calls[0] as [string, RequestInit]
    expect(url).toContain('/v1/test')
    expect((init.headers as Record<string, string>)['Authorization']).toBeUndefined()
    expect(result).toEqual({ id: '1' })
  })

  it('injeta o Authorization header quando há token', async () => {
    tokenStore.set('meu-access-token')
    const { apiClient } = await import('#/shared/lib/api-client')
    mockFetch.mockResolvedValueOnce(mockResponse({ data: { id: '1' } }))

    await apiClient.get('/v1/test')

    const [, init] = mockFetch.mock.calls[0] as [string, RequestInit]
    expect((init.headers as Record<string, string>)['Authorization']).toBe('Bearer meu-access-token')
  })

  it('lança ApiError tipado em resposta de erro', async () => {
    const { apiClient, ApiClientError } = await import('#/shared/lib/api-client')
    mockFetch
      .mockResolvedValueOnce(mockResponse({ code: 'resource.not_found', message: 'Not found' }, 404))
      .mockResolvedValueOnce(mockResponse({ code: 'resource.not_found', message: 'Not found' }, 404))

    await expect(apiClient.get('/v1/missing')).rejects.toThrow(ApiClientError)
    await expect(apiClient.get('/v1/missing')).rejects.toMatchObject({
      code: 'resource.not_found',
      status: 404,
    })
  })

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
  })

  it('lança erro e limpa o token quando a renovação falha', async () => {
    tokenStore.set('token-expirado')
    const { apiClient, ApiClientError } = await import('#/shared/lib/api-client')

    const renewToken = vi.fn().mockRejectedValue(new Error('refresh failed'))
    apiClient.setRenewTokenCallback(renewToken)

    mockFetch.mockResolvedValueOnce(
      mockResponse({ code: 'request.unauthorized', message: 'Unauthorized' }, 401),
    )

    await expect(apiClient.get('/v1/test')).rejects.toThrow(ApiClientError)
    expect(tokenStore.get()).toBeNull()
  })

  it('faz POST com body JSON', async () => {
    const { apiClient } = await import('#/shared/lib/api-client')
    mockFetch.mockResolvedValueOnce(mockResponse({ data: { videoId: 'abc' } }, 201))

    const result = await apiClient.post('/v1/users/joao/channels/meu-canal/videos', {
      title: 'Meu Vídeo',
      tags: [],
      visibility: 0,
    })

    const [, init] = mockFetch.mock.calls[0] as [string, RequestInit]
    expect(init.method).toBe('POST')
    expect(JSON.parse(init.body as string)).toEqual({ title: 'Meu Vídeo', tags: [], visibility: 0 })
    expect(result).toEqual({ videoId: 'abc' })
  })

  it('retorna undefined em respostas 204', async () => {
    const { apiClient } = await import('#/shared/lib/api-client')
    mockFetch.mockResolvedValueOnce(new Response(null, { status: 204 }))

    const result = await apiClient.delete('/v1/users/joao/channels/meu-canal/follow')

    expect(result).toBeUndefined()
  })
})
