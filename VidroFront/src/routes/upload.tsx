import { createFileRoute, redirect } from '@tanstack/react-router'
import { UploadVideoForm } from '#/features/videos/components/UploadVideoForm'
import { useCurrentUser } from '#/features/users/hooks'
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
  component: UploadPage,
})

function UploadPage() {
  const { data: profile, isPending, isError, error } = useCurrentUser()

  if (isPending) {
    return <main className="page-container py-8"><p>Loading…</p></main>
  }

  if (isError) {
    return (
      <main className="page-container py-8">
        <p className="text-destructive">{error.message}</p>
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
