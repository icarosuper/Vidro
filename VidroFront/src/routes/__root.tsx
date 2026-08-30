import {
  HeadContent,
  Outlet,
  Scripts,
  createRootRouteWithContext,
} from '@tanstack/react-router'
import { TanStackRouterDevtoolsPanel } from '@tanstack/react-router-devtools'
import { TanStackDevtools } from '@tanstack/react-devtools'
import type { QueryClient } from '@tanstack/react-query'
import { Toaster } from 'sonner'
import { Header } from '../components/Header'
import TanStackQueryDevtools from '../integrations/tanstack-query/devtools'
import { AuthModal } from '../features/auth/components/AuthModal'
import { AuthModalProvider, AuthProvider } from '../features/auth/hooks'
import { getInitialToken, renewToken } from '../features/auth/server'
import { apiClient } from '../shared/lib/api-client'
import { tokenStore } from '../shared/lib/token-store'
import appCss from '../styles.css?url'

interface MyRouterContext {
  queryClient: QueryClient
}

export const Route = createRootRouteWithContext<MyRouterContext>()({
  head: () => ({
    meta: [
      { charSet: 'utf-8' },
      { name: 'viewport', content: 'width=device-width, initial-scale=1' },
      { title: 'Vidro' },
    ],
    links: [{ rel: 'stylesheet', href: appCss }],
  }),
  beforeLoad: async () => {
    const isServer = typeof window === 'undefined'
    if (!isServer) return { accessToken: null as string | null }

    try {
      const accessToken = await getInitialToken()
      return { accessToken }
    } catch {
      return { accessToken: null as string | null }
    }
  },
  loader: ({ context }) => {
    return { initialToken: context.accessToken }
  },
  shellComponent: RootDocument,
  component: RootApp,
})

function RootApp() {
  const { initialToken } = Route.useLoaderData()

  const isClient = typeof window !== 'undefined'
  const tokenAlreadySet = tokenStore.get() !== null
  if (isClient && !tokenAlreadySet && initialToken !== null) {
    tokenStore.set(initialToken)
    apiClient.setRenewTokenCallback(() => renewToken())
  }

  return (
    <AuthProvider initialIsAuthenticated={initialToken !== null}>
      <AuthModalProvider>
        <Header />
        <Outlet />
        <AuthModal />
        <Toaster richColors />
      </AuthModalProvider>
    </AuthProvider>
  )
}

function RootDocument({ children }: { children: React.ReactNode }) {
  return (
    <html lang="pt-BR">
      <head>
        <HeadContent />
      </head>
      <body>
        {children}
        <TanStackDevtools
          config={{ position: 'bottom-right' }}
          plugins={[
            { name: 'Tanstack Router', render: <TanStackRouterDevtoolsPanel /> },
            TanStackQueryDevtools,
          ]}
        />
        <Scripts />
      </body>
    </html>
  )
}
