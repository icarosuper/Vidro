import type { ApiError } from '#/shared/types'
import { tokenStore } from './token-store'

export class ApiClientError extends Error {
  constructor(
    public readonly code: string,
    public readonly message: string,
    public readonly status: number,
  ) {
    super(message)
    this.name = 'ApiClientError'
  }
}

type RenewTokenCallback = () => Promise<string>

// Loaders run this on the server during SSR and again in the browser. In Docker the two
// need different hosts: the browser reaches the API on the published port, the SSR process
// reaches it by service name. import.meta.env is baked at build time (browser value);
// process.env is read at runtime on the server only — same split as `features/*/server.ts`.
const isServer = typeof window === 'undefined'
const serverApiUrl = isServer ? process.env.VITE_API_URL : undefined
const BASE_URL = serverApiUrl ?? import.meta.env.VITE_API_URL ?? 'http://localhost:5000'

let renewTokenCallback: RenewTokenCallback | null = null

async function request<T>(
  method: string,
  path: string,
  body?: unknown,
  signal?: AbortSignal,
  isRetry = false,
): Promise<T> {
  const token = tokenStore.get()

  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
  }
  if (token) {
    headers['Authorization'] = `Bearer ${token}`
  }

  const response = await fetch(`${BASE_URL}${path}`, {
    method,
    headers,
    body: body !== undefined ? JSON.stringify(body) : undefined,
    signal,
  })

  // 204 No Content
  if (response.status === 204) {
    return undefined as T
  }

  // 401 — tentar renovar o token uma vez
  if (response.status === 401 && !isRetry && renewTokenCallback) {
    try {
      const newToken = await renewTokenCallback()
      tokenStore.set(newToken)
      return request<T>(method, path, body, signal, true)
    } catch {
      tokenStore.clear()
      const data = (await response.json()) as ApiError
      throw new ApiClientError(data.code, data.message, response.status)
    }
  }

  const data = await response.json()

  if (!response.ok) {
    const error = data as ApiError
    throw new ApiClientError(error.code, error.message, response.status)
  }

  // Todas as respostas de sucesso têm formato { data: T }
  return (data as { data: T }).data
}

async function uploadRequest<T>(
  method: string,
  path: string,
  formData: FormData,
  signal?: AbortSignal,
): Promise<T> {
  const token = tokenStore.get()

  const headers: Record<string, string> = {}
  if (token) {
    headers['Authorization'] = `Bearer ${token}`
  }
  // Do NOT set Content-Type — browser sets it with the multipart boundary

  const response = await fetch(`${BASE_URL}${path}`, {
    method,
    headers,
    body: formData,
    signal,
  })

  if (response.status === 204) {
    return undefined as T
  }

  const data = await response.json()

  if (!response.ok) {
    const error = data as ApiError
    throw new ApiClientError(error.code, error.message, response.status)
  }

  return (data as { data: T }).data
}

export const apiClient = {
  get: <T>(path: string, signal?: AbortSignal) =>
    request<T>('GET', path, undefined, signal),

  post: <T>(path: string, body?: unknown, signal?: AbortSignal) =>
    request<T>('POST', path, body, signal),

  put: <T>(path: string, body?: unknown, signal?: AbortSignal) =>
    request<T>('PUT', path, body, signal),

  delete: <T>(path: string, signal?: AbortSignal) =>
    request<T>('DELETE', path, undefined, signal),

  upload: <T>(path: string, formData: FormData, signal?: AbortSignal) =>
    uploadRequest<T>('POST', path, formData, signal),

  setRenewTokenCallback: (cb: RenewTokenCallback | null) => {
    renewTokenCallback = cb
  },
}
