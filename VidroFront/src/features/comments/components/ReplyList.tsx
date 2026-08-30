import { useState } from 'react'
import { ThumbsDown, ThumbsUp } from 'lucide-react'
import { Avatar, AvatarFallback } from '#/components/ui/avatar'
import { Button } from '#/components/ui/button'
import { Textarea } from '#/components/ui/textarea'
import { useIsAuthenticated } from '#/features/auth/hooks'
import { ReactionType } from '#/shared/types'
import { useAddComment, useDeleteComment, useEditComment, useReactToComment, useRemoveCommentReaction, useReplies } from '../hooks'
import type { ReplySummary } from '../types'
import { ExpandableText } from './ExpandableText'

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

type ReplyItemProps = {
  reply: ReplySummary
  videoId: string
  parentCommentId: string
  currentUserId: string | undefined
  isAuthenticated: boolean
}

function ReplyItem({ reply, videoId, parentCommentId, currentUserId, isAuthenticated }: ReplyItemProps) {
  const [isEditing, setIsEditing] = useState(false)
  const [editContent, setEditContent] = useState(reply.content ?? '')
  const editComment = useEditComment(videoId)
  const deleteComment = useDeleteComment(videoId)
  const reactToComment = useReactToComment(videoId)
  const removeReaction = useRemoveCommentReaction(videoId)

  const isOwner = !!currentUserId && currentUserId === reply.userId
  const isMutating = reactToComment.isPending || removeReaction.isPending

  function handleReact(type: ReactionType) {
    if (!isAuthenticated) return
    const isActive = reply.userReaction?.id === type
    if (isActive) {
      removeReaction.mutate({ commentId: reply.commentId, parentCommentId })
    } else {
      reactToComment.mutate({ commentId: reply.commentId, type, parentCommentId })
    }
  }

  async function handleEdit() {
    if (!editContent.trim()) return
    await editComment.mutateAsync({ commentId: reply.commentId, content: editContent.trim() })
    setIsEditing(false)
  }

  function handleDelete() {
    deleteComment.mutate(reply.commentId)
  }

  const initial = reply.username.charAt(0).toUpperCase()

  return (
    <div className="flex gap-3">
      <Avatar className="h-7 w-7 shrink-0">
        <AvatarFallback className="text-xs">{initial}</AvatarFallback>
      </Avatar>
      <div className="flex-1 min-w-0">
        <div className="flex items-baseline gap-2 flex-wrap">
          <span className="text-sm font-medium">{reply.username}</span>
          <span className="text-xs text-muted-foreground">{formatRelativeDate(reply.createdAt)}</span>
          {reply.updatedAt && (
            <span className="text-xs text-muted-foreground">(edited)</span>
          )}
        </div>

        {isEditing ? (
          <div className="mt-1 space-y-2">
            <Textarea
              value={editContent}
              onChange={(e) => setEditContent(e.target.value)}
              rows={2}
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
        ) : reply.isDeleted ? (
          <p className="mt-0.5 text-sm text-muted-foreground italic">[deleted]</p>
        ) : (
          <ExpandableText text={reply.content!} className="mt-0.5 text-sm" />
        )}

        {!reply.isDeleted && (
          <div className="mt-1 flex items-center gap-1">
            <Button
              variant={reply.userReaction?.id === ReactionType.Like ? 'default' : 'ghost'}
              size="sm"
              className="h-7 px-2"
              onClick={() => handleReact(ReactionType.Like)}
              disabled={isMutating || !isAuthenticated}
            >
              <ThumbsUp className="h-3.5 w-3.5" />
              {reply.likeCount > 0 && <span className="ml-1 text-xs">{reply.likeCount}</span>}
            </Button>
            <Button
              variant={reply.userReaction?.id === ReactionType.Dislike ? 'default' : 'ghost'}
              size="sm"
              className="h-7 px-2"
              onClick={() => handleReact(ReactionType.Dislike)}
              disabled={isMutating || !isAuthenticated}
            >
              <ThumbsDown className="h-3.5 w-3.5" />
              {reply.dislikeCount > 0 && <span className="ml-1 text-xs">{reply.dislikeCount}</span>}
            </Button>
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
          </div>
        )}
      </div>
    </div>
  )
}

type ReplyListProps = {
  commentId: string
  videoId: string
  currentUserId: string | undefined
  initialShowForm?: boolean
}

export function ReplyList({ commentId, videoId, currentUserId, initialShowForm = false }: ReplyListProps) {
  const isAuthenticated = useIsAuthenticated()
  const { data, isPending, fetchNextPage, hasNextPage, isFetchingNextPage } = useReplies(commentId, true)
  const addComment = useAddComment(videoId)
  const [replyContent, setReplyContent] = useState('')
  const [showReplyForm, setShowReplyForm] = useState(initialShowForm)

  const replies = data?.pages.flatMap((page) => page.replies) ?? []

  async function handleSubmitReply() {
    if (!replyContent.trim()) return
    await addComment.mutateAsync({ content: replyContent.trim(), parentCommentId: commentId })
    setReplyContent('')
    setShowReplyForm(false)
  }

  if (isPending) {
    return <p className="pl-10 text-xs text-muted-foreground">Loading replies…</p>
  }

  return (
    <div className="pl-10 space-y-3">
      {replies.map((reply) => (
        <ReplyItem
          key={reply.commentId}
          reply={reply}
          videoId={videoId}
          parentCommentId={commentId}
          currentUserId={currentUserId}
          isAuthenticated={isAuthenticated}
        />
      ))}

      {hasNextPage && (
        <Button
          variant="ghost"
          size="sm"
          className="text-xs"
          onClick={() => fetchNextPage()}
          disabled={isFetchingNextPage}
        >
          {isFetchingNextPage ? 'Loading…' : 'Load more replies'}
        </Button>
      )}

      {isAuthenticated && (
        <>
          {showReplyForm ? (
            <div className="space-y-2">
              <Textarea
                value={replyContent}
                onChange={(e) => setReplyContent(e.target.value)}
                placeholder="Add a reply…"
                rows={2}
                className="text-sm"
              />
              <div className="flex gap-2">
                <Button size="sm" onClick={handleSubmitReply} disabled={addComment.isPending}>
                  Reply
                </Button>
                <Button size="sm" variant="ghost" onClick={() => setShowReplyForm(false)}>
                  Cancel
                </Button>
              </div>
            </div>
          ) : (
            <Button variant="ghost" size="sm" className="text-xs" onClick={() => setShowReplyForm(true)}>
              Reply
            </Button>
          )}
        </>
      )}
    </div>
  )
}
