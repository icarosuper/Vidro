import { useMutation, useQueryClient } from '@tanstack/react-query'
import {
  createContext,
  useCallback,
  useContext,
  useState,
  useSyncExternalStore,
} from 'react'
import type { ReactNode } from 'react'
import { apiClient } from '#/shared/lib/api-client'
import { tokenStore } from '#/shared/lib/token-store'
import { toastApiError } from '#/shared/lib/toast-error'
import { renewToken, serverSignIn, serverSignOut, serverSignUp } from './server'
import type { SignInRequest, SignUpRequest } from './types'

// ─── Auth state ───────────────────────────────────────────────────────────────

type AuthStateContextValue = { initialIsAuthenticated: boolean }

const AuthStateContext = createContext<AuthStateContextValue>({
  initialIsAuthenticated: false,
})

type AuthProviderProps = {
  initialIsAuthenticated: boolean
  children: ReactNode
}

export function AuthProvider({ initialIsAuthenticated, children }: AuthProviderProps) {
  return (
    <AuthStateContext.Provider value={{ initialIsAuthenticated }}>
      {children}
    </AuthStateContext.Provider>
  )
}

export function useIsAuthenticated(): boolean {
  const { initialIsAuthenticated } = useContext(AuthStateContext)

  return useSyncExternalStore(
    tokenStore.subscribe,
    () => tokenStore.get() !== null,
    () => initialIsAuthenticated,
  )
}

// ─── Sign in ──────────────────────────────────────────────────────────────────

export function useSignIn() {
  return useMutation({
    mutationFn: async (credentials: SignInRequest) => {
      const accessToken = await serverSignIn({ data: credentials })
      tokenStore.set(accessToken)
      apiClient.setRenewTokenCallback(() => renewToken())
      return accessToken
    },
  })
}

// ─── Sign up ──────────────────────────────────────────────────────────────────

export function useSignUp() {
  return useMutation({
    mutationFn: (request: SignUpRequest) => serverSignUp({ data: request }),
  })
}

// ─── Sign out ─────────────────────────────────────────────────────────────────

export function useSignOut() {
  const queryClient = useQueryClient()

  return useMutation({
    onError: toastApiError,
    mutationFn: async () => {
      const accessToken = tokenStore.get()
      tokenStore.clear()
      apiClient.setRenewTokenCallback(null)
      queryClient.clear()
      if (accessToken) {
        await serverSignOut({ data: { accessToken } })
      }
    },
  })
}

// ─── Auth modal context ───────────────────────────────────────────────────────

type AuthModalView = 'signIn' | 'signUp'

type AuthModalContextValue = {
  isOpen: boolean
  view: AuthModalView
  openSignIn: () => void
  openSignUp: () => void
  close: () => void
}

const AuthModalContext = createContext<AuthModalContextValue>({
  isOpen: false,
  view: 'signIn',
  openSignIn: () => {},
  openSignUp: () => {},
  close: () => {},
})

export function useAuthModal() {
  return useContext(AuthModalContext)
}

type AuthModalProviderProps = {
  children: ReactNode
}

export function AuthModalProvider({ children }: AuthModalProviderProps) {
  const [isOpen, setIsOpen] = useState(false)
  const [view, setView] = useState<AuthModalView>('signIn')

  const openSignIn = useCallback(() => {
    setView('signIn')
    setIsOpen(true)
  }, [])

  const openSignUp = useCallback(() => {
    setView('signUp')
    setIsOpen(true)
  }, [])

  const close = useCallback(() => setIsOpen(false), [])

  return (
    <AuthModalContext.Provider value={{ isOpen, view, openSignIn, openSignUp, close }}>
      {children}
    </AuthModalContext.Provider>
  )
}
