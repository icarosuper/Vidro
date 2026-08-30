import { apiClient } from '#/shared/lib/api-client'
import type { UserProfile } from './types'

export function getMe(signal?: AbortSignal) {
  return apiClient.get<UserProfile>('/v1/users/me', signal)
}

type UploadAvatarResponse = {
  uploadUrl: string
  uploadExpiresAt: string
}

export async function uploadAvatar(file: File, signal?: AbortSignal) {
  const { uploadUrl } = await apiClient.post<UploadAvatarResponse>(
    '/v1/users/me/avatar',
    undefined,
    signal,
  )

  // Upload the file directly to MinIO using the presigned URL
  // No Authorization header — the presigned URL already carries the credentials
  await fetch(uploadUrl, {
    method: 'PUT',
    body: file,
    headers: { 'Content-Type': file.type },
    signal,
  })
}
