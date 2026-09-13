// @vitest-environment jsdom

import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { CommentList } from '#/features/comments/components/CommentList'
import type { CommentSummary } from '#/features/comments/types'

const useIsAuthenticated = vi.fn(() => true)
const useComments = vi.fn()

vi.mock('#/features/auth/hooks', () => ({
  useIsAuthenticated: () => useIsAuthenticated(),
}))

function idleMutation() {
  return { mutate: vi.fn(), mutateAsync: vi.fn(), isPending: false }
}

vi.mock('#/features/comments/hooks', () => ({
  useComments: (videoId: string, sort: 0 | 1) => useComments(videoId, sort),
  useReplies: () => ({ data: undefined, isPending: false, isError: false }),
  useAddComment: idleMutation,
  useEditComment: idleMutation,
  useDeleteComment: idleMutation,
  useReactToComment: idleMutation,
  useRemoveCommentReaction: idleMutation,
}))

function buildComment(overrides: Partial<CommentSummary> = {}): CommentSummary {
  return {
    commentId: 'comment-1',
    userId: 'user-1',
    username: 'alice',
    content: 'first comment',
    isDeleted: false,
    likeCount: 0,
    dislikeCount: 0,
    replyCount: 0,
    userReaction: null,
    createdAt: new Date().toISOString(),
    updatedAt: null,
    ...overrides,
  }
}

function mockCommentsQuery(
  comments: CommentSummary[],
  state: { isPending?: boolean; isError?: boolean } = {},
) {
  useComments.mockReturnValue({
    data: { pages: [{ comments, nextCursor: null }] },
    isPending: state.isPending ?? false,
    isError: state.isError ?? false,
    fetchNextPage: vi.fn(),
    hasNextPage: false,
    isFetchingNextPage: false,
  })
}

afterEach(() => {
  cleanup()
  useIsAuthenticated.mockReturnValue(true)
  vi.clearAllMocks()
})

describe('CommentList', () => {
  it('offers Edit and Delete only on the signed-in user own comment', () => {
    mockCommentsQuery([
      buildComment({ commentId: 'mine', userId: 'user-1', username: 'alice' }),
      buildComment({
        commentId: 'theirs',
        userId: 'user-2',
        username: 'bob',
        content: 'someone else',
      }),
    ])

    render(<CommentList videoId="video-1" currentUserId="user-1" />)

    // Two comments are on screen, and exactly one of them owns the buttons — the pair that
    // shipped broken once because the caller passed a field the profile does not have.
    expect(screen.getByText('first comment')).toBeDefined()
    expect(screen.getByText('someone else')).toBeDefined()
    expect(screen.getAllByRole('button', { name: 'Edit' })).toHaveLength(1)
    expect(screen.getAllByRole('button', { name: 'Delete' })).toHaveLength(1)
  })

  it('hides Edit and Delete when nobody is signed in', () => {
    useIsAuthenticated.mockReturnValue(false)
    mockCommentsQuery([buildComment()])

    render(<CommentList videoId="video-1" currentUserId={undefined} />)

    expect(screen.queryByRole('button', { name: 'Edit' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Reply' })).toBeNull()
    expect(screen.queryByPlaceholderText('Add a comment…')).toBeNull()
  })

  it('renders a deleted comment as a tombstone, with no actions', () => {
    mockCommentsQuery([
      buildComment({ isDeleted: true, content: null, replyCount: 2 }),
    ])

    render(<CommentList videoId="video-1" currentUserId="user-1" />)

    expect(screen.getByText('[deleted]')).toBeDefined()
    expect(screen.queryByRole('button', { name: 'Reply' })).toBeNull()
    expect(screen.queryByRole('button', { name: '2 replies' })).toBeNull()
  })

  it('names the reaction buttons, which carry only an icon', () => {
    mockCommentsQuery([buildComment({ likeCount: 0, dislikeCount: 3 })])

    render(<CommentList videoId="video-1" currentUserId="user-1" />)

    // With no count to render, an icon-only button has no accessible name at all — the reader
    // announces "button". The count joins the name when there is one.
    expect(screen.getByRole('button', { name: 'Like' })).toBeDefined()
    expect(screen.getByRole('button', { name: 'Dislike 3' })).toBeDefined()
  })

  it('says the load failed instead of claiming there are no comments', () => {
    mockCommentsQuery([], { isError: true })

    render(<CommentList videoId="video-1" currentUserId="user-1" />)

    expect(screen.getByText('Failed to load comments.')).toBeDefined()
    expect(screen.queryByText('No comments yet.')).toBeNull()
  })
})
