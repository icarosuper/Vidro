import { describe, expect, it } from 'vitest'
import { ApiClientError } from '#/shared/lib/api-client'
import { getApiErrorMessage } from '#/shared/lib/error-messages'

describe('getApiErrorMessage', () => {
  // The code ExceptionMiddleware sends on a 500. It was mapped as `internal.server_error`, a code
  // the API never sends, so the user got the API's raw message instead.
  it('maps the API 500 code to a friendly message', () => {
    const error = new ApiClientError(
      'internal_error',
      'An unexpected error occurred.',
      500,
    )

    expect(getApiErrorMessage(error)).toBe(
      'Something went wrong. Please try again.',
    )
  })

  it('falls back to the API message for an unknown code', () => {
    const error = new ApiClientError('some.new_code', 'Raw message', 400)

    expect(getApiErrorMessage(error)).toBe('Raw message')
  })
})
