import { apiClient } from '#/shared/lib/api-client'
import type {
  Channel,
  ChannelsResponse,
  CreateChannelRequest,
  CreateChannelResponse,
  UpdateChannelRequest,
  UploadAvatarResponse,
} from './types'

export function getUserChannels(username: string, signal?: AbortSignal) {
  return apiClient.get<ChannelsResponse>(`/v1/users/${username}/channels`, signal)
}

export function getChannel(username: string, handle: string, signal?: AbortSignal) {
  return apiClient.get<Channel>(`/v1/users/${username}/channels/${handle}`, signal)
}

export function createChannel(data: CreateChannelRequest, signal?: AbortSignal) {
  return apiClient.post<CreateChannelResponse>('/v1/channels', data, signal)
}

export function updateChannel(handle: string, data: UpdateChannelRequest, signal?: AbortSignal) {
  return apiClient.put<void>(`/v1/channels/${handle}`, data, signal)
}

export function followChannel(username: string, handle: string, signal?: AbortSignal) {
  return apiClient.post<void>(`/v1/users/${username}/channels/${handle}/follow`, undefined, signal)
}

export function unfollowChannel(username: string, handle: string, signal?: AbortSignal) {
  return apiClient.delete<void>(`/v1/users/${username}/channels/${handle}/follow`, signal)
}

export async function uploadChannelAvatar(handle: string, file: File, signal?: AbortSignal) {
  const { uploadUrl } = await apiClient.post<UploadAvatarResponse>(
    `/v1/channels/${handle}/avatar`,
    undefined,
    signal,
  )

  await fetch(uploadUrl, {
    method: 'PUT',
    body: file,
    headers: { 'Content-Type': file.type },
    signal,
  })
}
