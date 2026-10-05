// @vitest-environment jsdom

import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { CreateChannelForm } from '#/features/channels/components/CreateChannelForm'
import { EditChannelForm } from '#/features/channels/components/EditChannelForm'
import { SubscribeButton } from '#/features/channels/components/SubscribeButton'
import type { Channel } from '#/features/channels/types'
import { ApiClientError } from '#/shared/lib/api-client'

type MutateCallbacks = {
  onSuccess?: () => void
  onError?: (error: unknown) => void
}

const followMutate = vi.fn()
const unfollowMutate = vi.fn()
const createMutate = vi.fn()
const updateMutate = vi.fn()

function idleMutation(mutate: ReturnType<typeof vi.fn>) {
  return { mutate, isPending: false, error: null }
}

vi.mock('#/features/channels/hooks', () => ({
  useFollowChannel: () => idleMutation(followMutate),
  useUnfollowChannel: () => idleMutation(unfollowMutate),
  useCreateChannel: () => idleMutation(createMutate),
  useUpdateChannel: () => idleMutation(updateMutate),
}))

/** Makes the mocked mutation settle the way the server would. */
function settleWith(
  mutate: ReturnType<typeof vi.fn>,
  outcome: { error: unknown } | 'success',
) {
  mutate.mockImplementation((_: unknown, callbacks: MutateCallbacks) => {
    if (outcome === 'success') callbacks.onSuccess?.()
    else callbacks.onError?.(outcome.error)
  })
}

function apiError(code: string) {
  return new ApiClientError(code, code, 409)
}

afterEach(() => {
  cleanup()
  vi.resetAllMocks()
})

describe('SubscribeButton', () => {
  it('flips to Subscribed once the follow succeeds', () => {
    settleWith(followMutate, 'success')

    render(<SubscribeButton username="alice" handle="my-channel" />)
    fireEvent.click(screen.getByRole('button', { name: 'Subscribe' }))

    expect(followMutate).toHaveBeenCalledOnce()
    expect(screen.getByRole('button', { name: 'Subscribed' })).toBeDefined()
  })

  it('treats "already following" as success, since the server state is what was asked', () => {
    settleWith(followMutate, { error: apiError('channel.already_following') })

    render(<SubscribeButton username="alice" handle="my-channel" />)
    fireEvent.click(screen.getByRole('button', { name: 'Subscribe' }))

    expect(screen.getByRole('button', { name: 'Subscribed' })).toBeDefined()
  })

  it('stays on Subscribe when the follow fails for another reason', () => {
    settleWith(followMutate, { error: apiError('channel.cannot_follow_own') })

    render(<SubscribeButton username="alice" handle="my-channel" />)
    fireEvent.click(screen.getByRole('button', { name: 'Subscribe' }))

    expect(screen.getByRole('button', { name: 'Subscribe' })).toBeDefined()
  })

  it('treats "not following" on unsubscribe as success', () => {
    settleWith(unfollowMutate, { error: apiError('channel.not_following') })

    render(
      <SubscribeButton
        username="alice"
        handle="my-channel"
        initialIsFollowing
      />,
    )
    fireEvent.click(screen.getByRole('button', { name: 'Subscribed' }))

    expect(unfollowMutate).toHaveBeenCalledOnce()
    expect(screen.getByRole('button', { name: 'Subscribe' })).toBeDefined()
  })

  it('follows the server value until the user clicks, then keeps the local one', () => {
    settleWith(unfollowMutate, 'success')

    const { rerender } = render(
      <SubscribeButton username="alice" handle="my-channel" />,
    )
    // The channel query resolves after mount and reports the user already follows.
    rerender(
      <SubscribeButton
        username="alice"
        handle="my-channel"
        initialIsFollowing
      />,
    )
    expect(screen.getByRole('button', { name: 'Subscribed' })).toBeDefined()

    fireEvent.click(screen.getByRole('button', { name: 'Subscribed' }))
    // A stale refetch must not undo the click the user just made.
    rerender(
      <SubscribeButton
        username="alice"
        handle="my-channel"
        initialIsFollowing={false}
      />,
    )
    rerender(
      <SubscribeButton
        username="alice"
        handle="my-channel"
        initialIsFollowing
      />,
    )

    expect(screen.getByRole('button', { name: 'Subscribe' })).toBeDefined()
  })
})

describe('CreateChannelForm', () => {
  it('rejects a handle with uppercase or spaces before calling the API', async () => {
    render(<CreateChannelForm username="alice" />)

    fireEvent.change(screen.getByLabelText('Name'), {
      target: { value: 'My Channel' },
    })
    fireEvent.change(screen.getByLabelText('Handle'), {
      target: { value: 'My Channel' },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Create channel' }))

    expect(
      await screen.findByText(
        'Handle may only contain lowercase letters, digits, and hyphens',
      ),
    ).toBeDefined()
    expect(createMutate).not.toHaveBeenCalled()
  })

  it('submits a valid handle', async () => {
    render(<CreateChannelForm username="alice" />)

    fireEvent.change(screen.getByLabelText('Name'), {
      target: { value: 'My Channel' },
    })
    fireEvent.change(screen.getByLabelText('Handle'), {
      target: { value: 'my-channel-2' },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Create channel' }))

    await waitFor(() => expect(createMutate).toHaveBeenCalledOnce())
    expect(createMutate.mock.calls[0]?.[0]).toMatchObject({
      handle: 'my-channel-2',
      name: 'My Channel',
    })
  })

  it('sends a blank description as null, like the edit form', async () => {
    render(<CreateChannelForm username="alice" />)

    fireEvent.change(screen.getByLabelText('Name'), {
      target: { value: 'My Channel' },
    })
    fireEvent.change(screen.getByLabelText('Handle'), {
      target: { value: 'my-channel' },
    })
    fireEvent.change(screen.getByLabelText('Description'), {
      target: { value: '   ' },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Create channel' }))

    await waitFor(() => expect(createMutate).toHaveBeenCalledOnce())
    expect(createMutate.mock.calls[0]?.[0]).toEqual({
      handle: 'my-channel',
      name: 'My Channel',
      description: null,
    })
  })

  it('explains a description over the limit', async () => {
    render(<CreateChannelForm username="alice" />)

    fireEvent.change(screen.getByLabelText('Name'), {
      target: { value: 'My Channel' },
    })
    fireEvent.change(screen.getByLabelText('Handle'), {
      target: { value: 'my-channel' },
    })
    fireEvent.change(screen.getByLabelText('Description'), {
      target: { value: 'a'.repeat(501) },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Create channel' }))

    expect(
      await screen.findByText('Description must be at most 500 characters'),
    ).toBeDefined()
    expect(createMutate).not.toHaveBeenCalled()
  })
})

describe('EditChannelForm', () => {
  const channel: Channel = {
    channelId: 'chan-1',
    handle: 'my-channel',
    name: 'My Channel',
    description: 'Old description',
    followerCount: 0,
    isFollowing: false,
    ownerId: 'user-1',
    ownerUsername: 'alice',
    avatarUrl: null,
    ownerAvatarUrl: null,
  }

  it('sends a blanked description as null, so the API clears it', async () => {
    render(<EditChannelForm channel={channel} onSuccess={vi.fn()} />)

    fireEvent.change(screen.getByLabelText('Description'), {
      target: { value: '   ' },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }))

    await waitFor(() => expect(updateMutate).toHaveBeenCalledOnce())
    expect(updateMutate.mock.calls[0]?.[0]).toEqual({
      name: 'My Channel',
      description: null,
    })
  })

  it('blocks an empty name', async () => {
    render(<EditChannelForm channel={channel} onSuccess={vi.fn()} />)

    fireEvent.change(screen.getByLabelText('Name'), { target: { value: '' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save changes' }))

    expect(await screen.findByText('Name is required')).toBeDefined()
    expect(updateMutate).not.toHaveBeenCalled()
  })
})
