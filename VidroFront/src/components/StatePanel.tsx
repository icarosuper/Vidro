import { Link } from '@tanstack/react-router'
import type { LucideIcon } from 'lucide-react'
import { RotateCw, SearchX, TriangleAlert } from 'lucide-react'
import type { ReactNode } from 'react'
import { Button } from '#/components/ui/button'
import { cn } from '#/lib/utils'

type StateFrameProps = {
  icon: LucideIcon
  tone: 'neutral' | 'destructive'
  title: string
  description?: ReactNode
  children?: ReactNode
  // Inside a section (comments, a tab): less padding and a smaller icon.
  compact?: boolean
  role?: 'alert'
  className?: string
}

// The one frame every "nothing to show" moment uses — an empty list, a failed query, a missing
// page — so they read as the same kind of thing: icon tile, short title, one line, an action.
function StateFrame({
  icon: Icon,
  tone,
  title,
  description,
  children,
  compact = false,
  role,
  className,
}: StateFrameProps) {
  return (
    <div
      role={role}
      className={cn(
        'flex flex-col items-center rounded-lg border border-dashed border-border text-center',
        compact ? 'px-4 py-8' : 'px-6 py-14',
        className,
      )}
    >
      <div
        className={cn(
          'flex items-center justify-center rounded-lg',
          compact ? 'size-10' : 'size-12',
          tone === 'destructive'
            ? 'bg-destructive/10 text-destructive'
            : 'bg-primary/10 text-primary',
        )}
      >
        <Icon className={compact ? 'size-5' : 'size-6'} />
      </div>
      <p
        className={cn(
          'mt-4 font-bold tracking-tight',
          compact ? 'text-sm' : 'text-base',
        )}
      >
        {title}
      </p>
      {description && (
        <p className="mt-1 max-w-sm text-sm text-muted-foreground">
          {description}
        </p>
      )}
      {children && <div className="mt-5 flex gap-2">{children}</div>}
    </div>
  )
}

type EmptyStateProps = {
  icon: LucideIcon
  title: string
  description?: ReactNode
  // The next step from here, when there is one — "Upload a video", "New playlist".
  action?: ReactNode
  compact?: boolean
  className?: string
}

export function EmptyState({ action, ...frame }: EmptyStateProps) {
  return (
    <StateFrame tone="neutral" {...frame}>
      {action}
    </StateFrame>
  )
}

type ErrorStateProps = {
  title?: string
  description?: ReactNode
  // Usually the failed query's `refetch`. Without it there is no button.
  onRetry?: () => void
  isRetrying?: boolean
  // Extra actions beside "Try again" — "Back to home" on a page that failed.
  children?: ReactNode
  compact?: boolean
  className?: string
}

export function ErrorState({
  title = 'Something went wrong',
  description = 'This could not be loaded. Trying again often works.',
  onRetry,
  isRetrying = false,
  children,
  ...frame
}: ErrorStateProps) {
  const hasActions = !!onRetry || !!children
  return (
    <StateFrame
      tone="destructive"
      icon={TriangleAlert}
      title={title}
      description={description}
      role="alert"
      {...frame}
    >
      {hasActions && (
        <>
          {onRetry && (
            <Button variant="outline" onClick={onRetry} disabled={isRetrying}>
              <RotateCw className={cn(isRetrying && 'animate-spin')} />
              Try again
            </Button>
          )}
          {children}
        </>
      )}
    </StateFrame>
  )
}

type NotFoundStateProps = {
  title: string
  description?: ReactNode
  icon?: LucideIcon
  className?: string
}

// What a page shows when the thing in its URL does not exist (or is private): no retry — trying
// again will not make it appear — and a way out.
export function NotFoundState({
  icon = SearchX,
  description = 'It may have been deleted, made private, or the link is wrong.',
  ...frame
}: NotFoundStateProps) {
  return (
    <EmptyState
      icon={icon}
      description={description}
      action={
        <Button variant="outline" asChild>
          <Link to="/" className="no-underline">
            Back to home
          </Link>
        </Button>
      }
      {...frame}
    />
  )
}

// A 404 from the API (ApiClientError) means "missing", not "failed": the page shows a not-found
// state instead of offering to retry. Read by shape, like RouteFallback does with the id.
export function isNotFoundError(error: unknown): boolean {
  return (error as { status?: unknown } | null)?.status === 404
}

type QueryErrorStateProps = {
  error: unknown
  // What is missing, worded for this page: "Video not found".
  notFoundTitle: string
  onRetry: () => void
  isRetrying?: boolean
}

// A page's whole-page query failed: not-found for a 404, otherwise a retryable error.
export function QueryErrorState({
  error,
  notFoundTitle,
  onRetry,
  isRetrying,
}: QueryErrorStateProps) {
  const isMissing = isNotFoundError(error)
  if (isMissing) return <NotFoundState title={notFoundTitle} />
  return <ErrorState onRetry={onRetry} isRetrying={isRetrying} />
}
