import type { ApiError } from '#/shared/api'
import { API_ERROR_CODES } from '#/shared/types'
import { CORRELATION_ID_HEADER, newCorrelationId } from './correlation-id'
import { tokenStore } from './token-store'

export class ApiClientError extends Error {
  constructor(
    public readonly code: string,
    public readonly message: string,
    public readonly status: number,
    /** Id sent on the failed request — the one the user can quote to support. */
    public readonly correlationId?: string,
  ) {
    super(message)
    this.name = 'ApiClientError'
  }
}

type RenewTokenCallback = () => Promise<string>

// Not every 4xx carries a body: the API's JWT middleware answers a missing or expired token with a
// bare 401, and the rate limiter a bare 429. Those get a code from the status, so callers always
// receive an ApiClientError instead of the SyntaxError `response.json()` throws on an empty body.
const BODILESS_ERRORS: Record<number, ApiError> = {
  401: { code: API_ERROR_CODES.UNAUTHORIZED, message: 'Unauthorized' },
  429: {
    code: API_ERROR_CODES.TOO_MANY_REQUESTS,
    message: 'Too many requests',
  },
}

function parseApiError(text: string): ApiError | null {
  try {
    const parsed: unknown = JSON.parse(text)
    const hasErrorCode =
      typeof parsed === 'object' &&
      parsed !== null &&
      typeof (parsed as ApiError).code === 'string'
    return hasErrorCode ? (parsed as ApiError) : null
  } catch {
    return null
  }
}

async function readApiError(response: Response): Promise<ApiError> {
  const body = parseApiError(await response.text())
  const fallback = BODILESS_ERRORS[response.status] ?? {
    code: API_ERROR_CODES.REQUEST_FAILED,
    message: `Request failed with status ${response.status}`,
  }
  return body ?? fallback
}

// Loaders run this on the server during SSR and again in the browser. In Docker the two
// need different hosts: the browser reaches the API on the published port, the SSR process
// reaches it by service name. import.meta.env is baked at build time (browser value);
// process.env is read at runtime on the server only — same split as `features/*/server.ts`.
const isServer = typeof window === 'undefined'
const serverApiUrl = isServer ? process.env.VITE_API_URL : undefined
const BASE_URL =
  serverApiUrl ?? import.meta.env.VITE_API_URL ?? 'http://localhost:5000'

let renewTokenCallback: RenewTokenCallback | null = null

async function request<T>(
  method: string,
  path: string,
  body?: unknown,
  signal?: AbortSignal,
  isRetry = false,
): Promise<T> {
  const token = tokenStore.get()

  const correlationId = newCorrelationId()

  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    [CORRELATION_ID_HEADER]: correlationId,
  }
  if (token) {
    headers.Authorization = `Bearer ${token}`
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

  // 401 — try renewing the token once
  if (response.status === 401 && !isRetry && renewTokenCallback) {
    try {
      const newToken = await renewTokenCallback()
      tokenStore.set(newToken)
      return request<T>(method, path, body, signal, true)
    } catch {
      tokenStore.clear()
      const error = await readApiError(response)
      throw new ApiClientError(
        error.code,
        error.message,
        response.status,
        correlationId,
      )
    }
  }

  if (!response.ok) {
    const error = await readApiError(response)
    throw new ApiClientError(
      error.code,
      error.message,
      response.status,
      correlationId,
    )
  }

  const data = await response.json()

  // Every success response has the shape { data: T }
  return (data as { data: T }).data
}

async function uploadRequest<T>(
  method: string,
  path: string,
  formData: FormData,
  signal?: AbortSignal,
): Promise<T> {
  const token = tokenStore.get()

  const correlationId = newCorrelationId()

  const headers: Record<string, string> = {
    [CORRELATION_ID_HEADER]: correlationId,
  }
  if (token) {
    headers.Authorization = `Bearer ${token}`
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

  if (!response.ok) {
    const error = await readApiError(response)
    throw new ApiClientError(
      error.code,
      error.message,
      response.status,
      correlationId,
    )
  }

  const data = await response.json()
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
