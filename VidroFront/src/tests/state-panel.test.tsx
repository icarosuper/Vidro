// @vitest-environment jsdom

import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { Film } from 'lucide-react'
import type { ReactNode } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  EmptyState,
  ErrorState,
  isNotFoundError,
  QueryErrorState,
} from '#/components/StatePanel'
import { ApiClientError } from '#/shared/lib/api-client'

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children }: { children: ReactNode }) => <a href="/">{children}</a>,
}))

afterEach(() => {
  cleanup()
})

describe('EmptyState', () => {
  it('shows the title, the description and the action', () => {
    render(
      <EmptyState
        icon={Film}
        title="No videos yet"
        description="Nothing here."
        action={<button type="button">Upload a video</button>}
      />,
    )

    expect(screen.getByText('No videos yet')).toBeDefined()
    expect(screen.getByText('Nothing here.')).toBeDefined()
    expect(screen.getByRole('button', { name: 'Upload a video' })).toBeDefined()
  })
})

describe('ErrorState', () => {
  it('is announced, and retries on click', () => {
    const onRetry = vi.fn()
    render(<ErrorState title="Could not load" onRetry={onRetry} />)

    expect(screen.getByRole('alert')).toBeDefined()
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }))
    expect(onRetry).toHaveBeenCalledOnce()
  })

  it('disables the retry while it runs', () => {
    render(<ErrorState onRetry={vi.fn()} isRetrying />)

    const retry = screen.getByRole('button', { name: 'Try again' })
    expect((retry as HTMLButtonElement).disabled).toBe(true)
  })

  it('has no button without a retry', () => {
    render(<ErrorState />)

    expect(screen.queryByRole('button')).toBeNull()
  })
})

describe('QueryErrorState', () => {
  it('treats a 404 as missing: no retry, a way home', () => {
    const missing = new ApiClientError('NOT_FOUND', 'Not found', 404)
    render(
      <QueryErrorState
        error={missing}
        notFoundTitle="Video not found"
        onRetry={vi.fn()}
      />,
    )

    expect(screen.getByText('Video not found')).toBeDefined()
    expect(screen.getByText('Back to home')).toBeDefined()
    expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull()
  })

  it('offers a retry for any other failure', () => {
    const failure = new ApiClientError('REQUEST_FAILED', 'Boom', 500)
    const onRetry = vi.fn()
    render(
      <QueryErrorState
        error={failure}
        notFoundTitle="Video not found"
        onRetry={onRetry}
      />,
    )

    expect(screen.queryByText('Video not found')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }))
    expect(onRetry).toHaveBeenCalledOnce()
  })
})

describe('isNotFoundError', () => {
  it('reads the status by shape, so a plain object counts too', () => {
    expect(isNotFoundError({ status: 404 })).toBe(true)
    expect(isNotFoundError(new Error('no status'))).toBe(false)
    expect(isNotFoundError(null)).toBe(false)
  })
})
