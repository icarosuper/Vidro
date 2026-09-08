import type { ErrorComponentProps } from '@tanstack/react-router'
import { Link, useRouter } from '@tanstack/react-router'
import { Button } from '#/components/ui/button'

/**
 * Fallback for a loader or render error in any route — wired as the router's
 * `defaultErrorComponent`, so a route only needs its own when it can do better.
 * Without it an invalid URL or a failing loader leaves a blank screen with no way back.
 */
export function RouteErrorFallback({ error }: ErrorComponentProps) {
  const router = useRouter()

  return (
    <main className="page-container py-16 text-center space-y-4">
      <h1 className="text-2xl font-bold">Something went wrong</h1>
      <p className="text-muted-foreground">
        This page could not be loaded. Trying again often works.
      </p>
      {import.meta.env.DEV && (
        <pre className="mx-auto max-w-xl overflow-x-auto rounded-md border border-border bg-muted p-3 text-left text-xs">
          {error.message}
        </pre>
      )}
      <div className="flex justify-center gap-2">
        <Button onClick={() => router.invalidate()}>Try again</Button>
        <Button variant="outline" asChild>
          <Link to="/">Back to home</Link>
        </Button>
      </div>
    </main>
  )
}

/** Fallback for an unknown URL, or a route that throws `notFound()`. */
export function RouteNotFoundFallback() {
  return (
    <main className="page-container py-16 text-center space-y-4">
      <h1 className="text-2xl font-bold">Page not found</h1>
      <p className="text-muted-foreground">
        This address does not exist, or what was here is gone.
      </p>
      <div className="flex justify-center">
        <Button asChild>
          <Link to="/">Back to home</Link>
        </Button>
      </div>
    </main>
  )
}
