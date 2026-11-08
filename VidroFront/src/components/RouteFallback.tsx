import type { ErrorComponentProps } from '@tanstack/react-router'
import { Link, useRouter } from '@tanstack/react-router'
import { ErrorState, NotFoundState } from '#/components/StatePanel'
import { Button } from '#/components/ui/button'

/**
 * The API stamps `X-Correlation-ID` on every log line of the failed request, and
 * `ApiClientError` carries the id we sent. Read it off the shape, not with `instanceof`:
 * an error thrown in an SSR loader reaches the browser serialized, without its class.
 */
function correlationIdOf(error: Error): string | undefined {
  const id = (error as { correlationId?: unknown }).correlationId
  return typeof id === 'string' ? id : undefined
}

/**
 * Fallback for a loader or render error in any route — wired as the router's
 * `defaultErrorComponent`, so a route only needs its own when it can do better.
 * Without it an invalid URL or a failing loader leaves a blank screen with no way back.
 */
export function RouteErrorFallback({ error }: ErrorComponentProps) {
  const router = useRouter()
  const correlationId = correlationIdOf(error)

  return (
    <main className="page-container space-y-4 py-8">
      <ErrorState
        description="This page could not be loaded. Trying again often works."
        onRetry={() => router.invalidate()}
      >
        <Button variant="ghost" asChild>
          <Link to="/" className="no-underline">
            Back to home
          </Link>
        </Button>
      </ErrorState>
      {import.meta.env.DEV && (
        <pre className="mx-auto max-w-xl overflow-x-auto rounded-md border border-border bg-muted p-3 text-left font-mono text-xs">
          {error.message}
        </pre>
      )}
      {correlationId && (
        <p className="text-center text-xs text-muted-foreground">
          Reference for support:{' '}
          <code className="rounded bg-muted px-1 py-0.5 font-mono select-all">
            {correlationId}
          </code>
        </p>
      )}
    </main>
  )
}

/** Fallback for an unknown URL, or a route that throws `notFound()`. */
export function RouteNotFoundFallback() {
  return (
    <main className="page-container py-8">
      <NotFoundState
        title="Page not found"
        description="This address does not exist, or what was here is gone."
      />
    </main>
  )
}
