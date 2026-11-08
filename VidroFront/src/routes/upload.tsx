import { createFileRoute, redirect } from '@tanstack/react-router'
import { ErrorState } from '#/components/StatePanel'
import { Skeleton } from '#/components/ui/skeleton'
import { useCurrentUser } from '#/features/users/hooks'
import { UploadVideoForm } from '#/features/videos/components/UploadVideoForm'
import { seo } from '#/shared/lib/seo'
import { tokenStore } from '#/shared/lib/token-store'

export const Route = createFileRoute('/upload')({
  beforeLoad: () => {
    const isServer = typeof window === 'undefined'
    if (isServer) return

    const isAuthenticated = tokenStore.get() !== null
    if (!isAuthenticated) {
      throw redirect({ to: '/' })
    }
  },
  head: () => ({
    meta: seo({
      title: 'Upload',
      description: 'Upload a video to your channel.',
      noIndex: true,
    }),
  }),
  component: UploadPage,
})

function UploadPage() {
  const {
    data: profile,
    isPending,
    isError,
    refetch,
    isRefetching,
  } = useCurrentUser()

  if (isPending) {
    return (
      <main className="page-container py-8">
        <Skeleton className="mb-6 h-8 w-48" />
        <Skeleton className="h-72 w-full max-w-2xl rounded-xl" />
      </main>
    )
  }

  if (isError) {
    return (
      <main className="page-container py-8">
        <ErrorState onRetry={() => refetch()} isRetrying={isRefetching} />
      </main>
    )
  }

  return (
    <main className="page-container py-8">
      <h1 className="mb-6 text-2xl font-bold">Upload video</h1>
      <UploadVideoForm username={profile.username} />
    </main>
  )
}
