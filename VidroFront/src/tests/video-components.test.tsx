// @vitest-environment jsdom

import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { EditVideoForm } from '#/features/videos/components/EditVideoForm'
import { UploadVideoForm } from '#/features/videos/components/UploadVideoForm'
import type { ChannelVideoSummary } from '#/features/videos/types'

const updateMutateAsync = vi.fn()
const createMutateAsync = vi.fn()

vi.mock('#/features/videos/hooks', () => ({
  useUpdateVideo: () => ({ mutateAsync: updateMutateAsync, isPending: false }),
  useCreateVideo: () => ({
    mutateAsync: createMutateAsync,
    isPending: false,
    isError: false,
  }),
  useUploadThumbnail: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useVideoStatus: () => ({ data: undefined }),
}))

vi.mock('#/features/videos/api', () => ({
  uploadVideoFile: vi.fn().mockResolvedValue(undefined),
}))

vi.mock('#/features/channels/hooks', () => ({
  useUserChannels: () => ({
    isPending: false,
    data: { channels: [{ channelId: 'chan-1', handle: 'mine', name: 'Mine' }] },
  }),
}))

const video: ChannelVideoSummary = {
  videoId: 'vid-1',
  channelHandle: 'mine',
  ownerUsername: 'alice',
  title: 'Old title',
  description: 'Old description',
  tags: [],
  visibility: { id: 0, value: 'Public' },
  status: { id: 2, value: 'Ready' },
  viewCount: 0,
  likeCount: 0,
  thumbnailUrls: [],
  channelAvatarUrl: null,
  createdAt: '2024-01-01T00:00:00Z',
}

afterEach(() => {
  cleanup()
  vi.resetAllMocks()
})

describe('EditVideoForm', () => {
  function submitWithDescription(description: string) {
    render(<EditVideoForm video={video} onSuccess={() => {}} />)
    fireEvent.change(screen.getByLabelText('Description'), {
      target: { value: description },
    })
    fireEvent.click(screen.getByRole('button', { name: /save/i }))
  }

  it('sends a blank description as null', async () => {
    submitWithDescription('   ')

    await waitFor(() => expect(updateMutateAsync).toHaveBeenCalledOnce())
    expect(updateMutateAsync.mock.calls[0]?.[0].description).toBeNull()
  })

  it('explains a description over the limit', async () => {
    submitWithDescription('a'.repeat(5001))

    expect(
      await screen.findByText('Description must be at most 5000 characters'),
    ).toBeDefined()
    expect(updateMutateAsync).not.toHaveBeenCalled()
  })

  it('explains a title over the limit', async () => {
    render(<EditVideoForm video={video} onSuccess={() => {}} />)
    fireEvent.change(screen.getByLabelText('Title'), {
      target: { value: 'a'.repeat(101) },
    })
    fireEvent.click(screen.getByRole('button', { name: /save/i }))

    expect(
      await screen.findByText('Title must be at most 100 characters'),
    ).toBeDefined()
  })
})

describe('UploadVideoForm', () => {
  function fillAndSubmit(fields: { title: string; description: string }) {
    const { container } = render(<UploadVideoForm username="alice" />)
    const fileInput = container.querySelector('input[type="file"]')
    fireEvent.change(fileInput as Element, {
      target: { files: [new File(['x'], 'v.mp4', { type: 'video/mp4' })] },
    })
    fireEvent.change(screen.getByLabelText('Title'), {
      target: { value: fields.title },
    })
    fireEvent.change(screen.getByLabelText('Description'), {
      target: { value: fields.description },
    })
    fireEvent.submit(container.querySelector('form') as Element)
  }

  it('keeps the channel select controlled from the first render', async () => {
    const consoleWarn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    render(<UploadVideoForm username="alice" />)

    await screen.findAllByText('Mine')
    const warnings = consoleWarn.mock.calls.filter((call) =>
      String(call[0]).includes('uncontrolled'),
    )
    expect(warnings).toHaveLength(0)
  })

  it('sends a blank description as null', async () => {
    createMutateAsync.mockRejectedValue(new Error('stop here'))
    fillAndSubmit({ title: 'T', description: '   ' })

    await waitFor(() => expect(createMutateAsync).toHaveBeenCalledOnce())
    expect(createMutateAsync.mock.calls[0]?.[0].description).toBeNull()
  })

  it('explains a title and description over the limit', async () => {
    fillAndSubmit({ title: 'a'.repeat(101), description: 'a'.repeat(5001) })

    expect(
      await screen.findByText('Title must be at most 100 characters'),
    ).toBeDefined()
    expect(
      screen.getByText('Description must be at most 5000 characters'),
    ).toBeDefined()
    expect(createMutateAsync).not.toHaveBeenCalled()
  })
})
