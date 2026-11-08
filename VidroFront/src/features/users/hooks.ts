import {
  skipToken,
  useMutation,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query'
import { useIsAuthenticated } from '#/features/auth/hooks'
import { getMe, uploadAvatar } from './api'

export const userKeys = {
  me: () => ['users', 'me'] as const,
}

// Only asks for /users/me with a session. Signed out it used to fire anyway, cache the 401, and
// nothing invalidated it on sign in — so the avatar and "Your channels" stayed missing until the
// next remount. Gating on the session makes the query start the moment the token arrives.
export function useCurrentUser() {
  const isAuthenticated = useIsAuthenticated()
  return useQuery({
    queryKey: userKeys.me(),
    queryFn: isAuthenticated ? ({ signal }) => getMe(signal) : skipToken,
  })
}

export function useUploadAvatar() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (file: File) => uploadAvatar(file),
    onSuccess: () => {
      // Refetch to get the new presigned avatarUrl from the server
      queryClient.invalidateQueries({ queryKey: userKeys.me() })
    },
  })
}
