// @vitest-environment jsdom

import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, renderHook, waitFor } from '@testing-library/react'
import type { ReactNode } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useCurrentUser } from '#/features/users/hooks'

let isAuthenticated = false

vi.mock('#/features/auth/hooks', () => ({
  useIsAuthenticated: () => isAuthenticated,
}))

const getMe = vi.fn()

vi.mock('#/features/users/api', () => ({
  getMe: (signal?: AbortSignal) => getMe(signal),
  uploadAvatar: vi.fn(),
}))

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  )
}

afterEach(() => {
  cleanup()
  vi.clearAllMocks()
  isAuthenticated = false
})

describe('useCurrentUser', () => {
  it('does not ask for the current user without a session', () => {
    renderHook(() => useCurrentUser(), { wrapper: createWrapper() })

    expect(getMe).not.toHaveBeenCalled()
  })

  it('fetches the current user as soon as the session appears', async () => {
    getMe.mockResolvedValue({ userId: 'u1', username: 'priya' })
    const { result, rerender } = renderHook(() => useCurrentUser(), {
      wrapper: createWrapper(),
    })

    isAuthenticated = true
    rerender()

    await waitFor(() => expect(result.current.data?.username).toBe('priya'))
    expect(getMe).toHaveBeenCalledTimes(1)
  })
})
