import { useState } from 'react'
import { ThumbsDown, ThumbsUp } from 'lucide-react'
import { Avatar, AvatarFallback } from '#/components/ui/avatar'
import { Button } from '#/components/ui/button'
import { Textarea } from '#/components/ui/textarea'
import { useIsAuthenticated } from '#/features/auth/hooks'
import { ReactionType } from '#/shared/types'
import { ExpandableText } from './ExpandableText'
import {
  useAddComment,
  useComments,
  useDeleteComment,
  useEditComment,
  useReactToComment,
  useRemoveCommentReaction,
} from '../hooks'
import type { CommentSummary } from '../types'
import { ReplyList } from './ReplyList'

function formatRelativeDate(isoDate: string): string {
  const diff = Date.now() - new Date(isoDate).getTime()
  const minutes = Math.floor(diff / 60_000)
  if (minutes < 60) return `${minutes}m ago`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours}h ago`
  const days = Math.floor(hours / 24)
  if (days < 30) return `${days}d ago`
  return new Date(isoDate).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })
}

type CommentItemProps = {
  comment: CommentSummary
  videoId: string
  currentUserId: string | undefined
  isAuthenticated: boolean
}

function CommentItem({ comment, videoId, currentUserId, isAuthenticated }: CommentItemProps) {
  const [showReplies, setShowReplies] = useState(false)
  const [openReplyForm, setOpenReplyForm] = useState(false)
  const [isEditing, setIsEditing] = useState(false)
  const [editContent, setEditContent] = useState(comment.content ?? '')
  const editComment = useEditComment(videoId)
  const deleteComment = useDeleteComment(videoId)
  const reactToComment = useReactToComment(videoId)
  const removeReaction = useRemoveCommentReaction(videoId)

  const isOwner = !!currentUserId && currentUserId === comment.userId
  const isMutating = reactToComment.isPending || removeReaction.isPending

  function handleReact(type: ReactionType) {
    if (!isAuthenticated) return
    const isActive = comment.userReaction?.id === type
    if (isActive) {
      removeReaction.mutate({ commentId: comment.commentId })
    } else {
      reactToComment.mutate({ commentId: comment.commentId, type })
    }
  }

  async function handleEdit() {
    if (!editContent.trim()) return
    await editComment.mutateAsync({ commentId: comment.commentId, content: editContent.trim() })
    setIsEditing(false)
  }

  function handleDelete() {
    deleteComment.mutate(comment.commentId)
  }

  const initial = comment.username.charAt(0).toUpperCase()

  return (
    <div className="flex gap-3">
      <Avatar className="h-8 w-8 shrink-0">
        <AvatarFallback className="text-sm">{initial}</AvatarFallback>
      </Avatar>
      <div className="flex-1 min-w-0">
        <div className="flex items-baseline gap-2 flex-wrap">
          <span className="text-sm font-medium">{comment.username}</span>
          <span className="text-xs text-muted-foreground">{formatRelativeDate(comment.createdAt)}</span>
          {comment.updatedAt && (
            <span className="text-xs text-muted-foreground">(edited)</span>
          )}
        </div>

        {isEditing ? (
          <div className="mt-1 space-y-2">
            <Textarea
              value={editContent}
              onChange={(e) => setEditContent(e.target.value)}
              rows={3}
              className="text-sm"
            />
            <div className="flex gap-2">
              <Button size="sm" onClick={handleEdit} disabled={editComment.isPending}>
                Save
              </Button>
              <Button size="sm" variant="ghost" onClick={() => setIsEditing(false)}>
                Cancel
              </Button>
            </div>
          </div>
        ) : comment.isDeleted ? (
          <p className="mt-0.5 text-sm text-muted-foreground italic">[deleted]</p>
        ) : (
          <ExpandableText text={comment.content!} className="mt-0.5 text-sm" />
        )}

        {!comment.isDeleted && (
          <div className="mt-1 flex items-center gap-1 flex-wrap">
            <Button
              variant={comment.userReaction?.id === ReactionType.Like ? 'default' : 'ghost'}
              size="sm"
              className="h-7 px-2"
              onClick={() => handleReact(ReactionType.Like)}
              disabled={isMutating || !isAuthenticated}
            >
              <ThumbsUp className="h-3.5 w-3.5" />
              {comment.likeCount > 0 && <span className="ml-1 text-xs">{comment.likeCount}</span>}
            </Button>
            <Button
              variant={comment.userReaction?.id === ReactionType.Dislike ? 'default' : 'ghost'}
              size="sm"
              className="h-7 px-2"
              onClick={() => handleReact(ReactionType.Dislike)}
              disabled={isMutating || !isAuthenticated}
            >
              <ThumbsDown className="h-3.5 w-3.5" />
              {comment.dislikeCount > 0 && <span className="ml-1 text-xs">{comment.dislikeCount}</span>}
            </Button>
            {comment.replyCount > 0 && (
              <Button
                variant="ghost"
                size="sm"
                className="h-7 px-2 text-xs"
                onClick={() => setShowReplies((v) => !v)}
              >
                {showReplies ? 'Hide' : `${comment.replyCount} ${comment.replyCount === 1 ? 'reply' : 'replies'}`}
              </Button>
            )}
            {isOwner && !isEditing && (
              <>
                <Button variant="ghost" size="sm" className="h-7 px-2 text-xs" onClick={() => setIsEditing(true)}>
                  Edit
                </Button>
                <Button
                  variant="ghost"
                  size="sm"
                  className="h-7 px-2 text-xs text-destructive hover:text-destructive"
                  onClick={handleDelete}
                  disabled={deleteComment.isPending}
                >
                  Delete
                </Button>
              </>
            )}
            {isAuthenticated && (
              <Button
                variant="ghost"
                size="sm"
                className="h-7 px-2 text-xs"
                onClick={() => {
                  setOpenReplyForm(true)
                  setShowReplies(true)
                }}
              >
                Reply
              </Button>
            )}
          </div>
        )}

        {showReplies && (
          <div className="mt-3">
            <ReplyList
              key={String(openReplyForm)}
              commentId={comment.commentId}
              videoId={videoId}
              currentUserId={currentUserId}
              initialShowForm={openReplyForm}
            />
          </div>
        )}
      </div>
    </div>
  )
}

type AddCommentFormProps = {
  videoId: string
}

function AddCommentForm({ videoId }: AddCommentFormProps) {
  const [content, setContent] = useState('')
  const addComment = useAddComment(videoId)

  async function handleSubmit() {
    if (!content.trim()) return
    await addComment.mutateAsync({ content: content.trim() })
    setContent('')
  }

  return (
    <div className="space-y-2">
      <Textarea
        value={content}
        onChange={(e) => setContent(e.target.value)}
        placeholder="Add a comment…"
        rows={3}
      />
      <div className="flex justify-end">
        <Button onClick={handleSubmit} disabled={addComment.isPending || !content.trim()}>
          {addComment.isPending ? 'Posting…' : 'Comment'}
        </Button>
      </div>
    </div>
  )
}

type CommentListProps = {
  videoId: string
  currentUserId: string | undefined
}

export function CommentList({ videoId, currentUserId }: CommentListProps) {
  const isAuthenticated = useIsAuthenticated()
  const [sort, setSort] = useState<0 | 1>(0)
  const { data, isPending, isError, fetchNextPage, hasNextPage, isFetchingNextPage } = useComments(videoId, sort)

  const comments = data?.pages.flatMap((page) => page.comments) ?? []

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h2 className="text-lg font-semibold">Comments</h2>
        <div className="flex gap-1">
          <Button
            variant={sort === 0 ? 'default' : 'ghost'}
            size="sm"
            onClick={() => setSort(0)}
          >
            Recent
          </Button>
          <Button
            variant={sort === 1 ? 'default' : 'ghost'}
            size="sm"
            onClick={() => setSort(1)}
          >
            Popular
          </Button>
        </div>
      </div>

      {isAuthenticated && <AddCommentForm videoId={videoId} />}

      {isPending && <p className="text-sm text-muted-foreground">Loading comments…</p>}
      {isError && <p className="text-sm text-destructive">Failed to load comments.</p>}

      {!isPending && comments.length === 0 && (
        <p className="text-sm text-muted-foreground">No comments yet.</p>
      )}

      <div className="space-y-6">
        {comments.map((comment) => (
          <CommentItem
            key={comment.commentId}
            comment={comment}
            videoId={videoId}
            currentUserId={currentUserId}
            isAuthenticated={isAuthenticated}
          />
        ))}
      </div>

      {hasNextPage && sort === 0 && (
        <div className="flex justify-center">
          <Button variant="outline" onClick={() => fetchNextPage()} disabled={isFetchingNextPage}>
            {isFetchingNextPage ? 'Loading…' : 'Load more comments'}
          </Button>
        </div>
      )}
    </div>
  )
}
