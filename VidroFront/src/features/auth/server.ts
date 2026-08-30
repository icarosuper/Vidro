import { createServerFn } from '@tanstack/react-start'
import { deleteCookie, getCookie, setCookie } from '@tanstack/react-start/server'
import { z } from 'zod'
import type { AuthTokens } from './types'

const REFRESH_TOKEN_COOKIE = 'vid_rt'

const API_URL = process.env.VITE_API_URL ?? 'http://localhost:5000'

async function fetchAuthApi<T>(
  path: string,
  body: unknown,
  accessToken?: string,
): Promise<T> {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
  }
  if (accessToken) {
    headers['Authorization'] = `Bearer ${accessToken}`
  }

  const response = await fetch(`${API_URL}${path}`, {
    method: 'POST',
    headers,
    body: JSON.stringify(body),
  })

  if (!response.ok) {
    const error = await response.json()
    throw new Error(error.message ?? 'Auth request failed')
  }

  return response.json()
}

export const serverSignUp = createServerFn({ method: 'POST' })
  .inputValidator(
    z.object({
      username: z.string(),
      email: z.string(),
      password: z.string(),
    }),
  )
  .handler(async ({ data }) => {
    await fetchAuthApi('/v1/auth/signup', data)
  })

export const serverSignIn = createServerFn({ method: 'POST' })
  .inputValidator(
    z.object({
      email: z.string(),
      password: z.string(),
    }),
  )
  .handler(async ({ data }) => {
    const { data: tokens } = await fetchAuthApi<{ data: AuthTokens }>(
      '/v1/auth/signin',
      data,
    )

    setCookie(REFRESH_TOKEN_COOKIE, tokens.refreshToken, {
      httpOnly: true,
      secure: process.env.NODE_ENV === 'production',
      sameSite: 'lax',
      maxAge: 60 * 60 * 24 * 30,
      path: '/',
    })

    return tokens.accessToken
  })

export const serverSignOut = createServerFn({ method: 'POST' })
  .inputValidator(z.object({ accessToken: z.string() }))
  .handler(async ({ data }) => {
    const refreshToken = getCookie(REFRESH_TOKEN_COOKIE)
    if (!refreshToken) return

    try {
      await fetchAuthApi('/v1/auth/signout', { refreshToken }, data.accessToken)
    } finally {
      deleteCookie(REFRESH_TOKEN_COOKIE, { path: '/' })
    }
  })

export const renewToken = createServerFn({ method: 'POST' }).handler(
  async () => {
    const refreshToken = getCookie(REFRESH_TOKEN_COOKIE)
    if (!refreshToken) throw new Error('No refresh token')

    const { data: tokens } = await fetchAuthApi<{ data: AuthTokens }>(
      '/v1/auth/renew-token',
      { refreshToken },
    )

    setCookie(REFRESH_TOKEN_COOKIE, tokens.refreshToken, {
      httpOnly: true,
      secure: process.env.NODE_ENV === 'production',
      sameSite: 'lax',
      maxAge: 60 * 60 * 24 * 30,
      path: '/',
    })

    return tokens.accessToken
  },
)

export const getInitialToken = createServerFn({ method: 'GET' }).handler(
  async () => {
    const refreshToken = getCookie(REFRESH_TOKEN_COOKIE)
    if (!refreshToken) return null

    try {
      const { data: tokens } = await fetchAuthApi<{ data: AuthTokens }>(
        '/v1/auth/renew-token',
        { refreshToken },
      )

      setCookie(REFRESH_TOKEN_COOKIE, tokens.refreshToken, {
        httpOnly: true,
        secure: process.env.NODE_ENV === 'production',
        sameSite: 'lax',
        maxAge: 60 * 60 * 24 * 30,
        path: '/',
      })

      return tokens.accessToken
    } catch {
      deleteCookie(REFRESH_TOKEN_COOKIE, { path: '/' })
      return null
    }
  },
)
