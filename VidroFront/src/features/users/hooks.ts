import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { getMe, uploadAvatar } from './api'

export const userKeys = {
  me: () => ['users', 'me'] as const,
}

export function useCurrentUser() {
  return useQuery({
    queryKey: userKeys.me(),
    queryFn: ({ signal }) => getMe(signal),
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
