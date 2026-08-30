import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

// BASE_URL is resolved once at module load, so every case needs a fresh import.
const mockFetch = vi.fn()
vi.stubGlobal('fetch', mockFetch)

const originalApiUrl = process.env.VITE_API_URL

async function requestedUrl(): Promise<string> {
  const { apiClient } = await import('#/shared/lib/api-client')
  mockFetch.mockResolvedValueOnce(
    new Response(JSON.stringify({ data: {} }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }),
  )
  await apiClient.get('/v1/test')
  return mockFetch.mock.calls[0][0] as string
}

beforeEach(() => {
  vi.resetModules()
  mockFetch.mockReset()
})

afterEach(() => {
  if (originalApiUrl === undefined) delete process.env.VITE_API_URL
  else process.env.VITE_API_URL = originalApiUrl
})

// These tests run under the node environment, so `window` is undefined — the SSR branch.
describe('api base url on the server', () => {
  it('prefers process.env at runtime, so Docker can point SSR at the internal host', async () => {
    process.env.VITE_API_URL = 'http://api:5000'

    expect(await requestedUrl()).toBe('http://api:5000/v1/test')
  })

  it('falls back to the build-time value when no runtime override is set', async () => {
    delete process.env.VITE_API_URL

    expect(await requestedUrl()).toBe('http://localhost:5000/v1/test')
  })
})
