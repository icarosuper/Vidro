import { ReactionType } from '#/shared/types'
import { apiClient } from '#/shared/lib/api-client'
import type {
  ChannelVideosPage,
  CreateVideoRequest,
  CreateVideoResponse,
  FeedPage,
  ThumbnailUploadResponse,
  TrendingResponse,
  UpdateVideoRequest,
  UpdateVideoResponse,
  Video,
} from './types'

export function getTrending(limit: number, signal?: AbortSignal) {
  return apiClient.get<TrendingResponse>(`/v1/videos/trending?limit=${limit}`, signal)
}

export function getFeed(limit: number, cursor: string | undefined, signal?: AbortSignal) {
  const params = new URLSearchParams({ limit: String(limit) })
  if (cursor) params.set('cursor', cursor)
  return apiClient.get<FeedPage>(`/v1/feed?${params.toString()}`, signal)
}

export function getVideo(videoId: string, signal?: AbortSignal) {
  return apiClient.get<Video>(`/v1/videos/${videoId}`, signal)
}

export function registerView(videoId: string, signal?: AbortSignal) {
  return apiClient.post<void>(`/v1/videos/${videoId}/view`, undefined, signal)
}

export function reactToVideo(videoId: string, type: ReactionType, signal?: AbortSignal) {
  return apiClient.post<void>(`/v1/videos/${videoId}/react`, { type }, signal)
}

export function removeReaction(videoId: string, signal?: AbortSignal) {
  return apiClient.delete<void>(`/v1/videos/${videoId}/react`, signal)
}

export function getChannelVideos(
  username: string,
  handle: string,
  limit: number,
  cursor: string | undefined,
  signal?: AbortSignal,
) {
  const params = new URLSearchParams({ limit: String(limit) })
  if (cursor) params.set('cursor', cursor)
  return apiClient.get<ChannelVideosPage>(`/v1/users/${username}/channels/${handle}/videos?${params.toString()}`, signal)
}

export function createVideo(
  username: string,
  handle: string,
  data: CreateVideoRequest,
  signal?: AbortSignal,
) {
  return apiClient.post<CreateVideoResponse>(`/v1/users/${username}/channels/${handle}/videos`, data, signal)
}

export function uploadVideoFile(
  uploadUrl: string,
  file: File,
  onProgress?: (progress: number) => void,
  signal?: AbortSignal,
): Promise<void> {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open('PUT', uploadUrl)
    xhr.setRequestHeader('Content-Type', file.type || 'video/mp4')

    if (onProgress) {
      xhr.upload.onprogress = (event) => {
        const canCompute = event.lengthComputable
        if (canCompute) {
          const progress = Math.round((event.loaded / event.total) * 100)
          onProgress(progress)
        }
      }
    }

    xhr.onload = () => {
      const uploadSucceeded = xhr.status >= 200 && xhr.status < 300
      if (uploadSucceeded) {
        resolve()
      } else {
        reject(new Error(`Upload failed with status ${xhr.status}`))
      }
    }

    xhr.onerror = () => reject(new Error('Network error during upload'))

    if (signal) {
      signal.addEventListener('abort', () => xhr.abort())
    }

    xhr.send(file)
  })
}

export function updateVideo(
  videoId: string,
  data: UpdateVideoRequest,
  signal?: AbortSignal,
) {
  return apiClient.put<UpdateVideoResponse>(`/v1/videos/${videoId}`, data, signal)
}

export async function uploadThumbnail(
  videoId: string,
  file: File,
  signal?: AbortSignal,
) {
  const { uploadUrl } = await apiClient.post<ThumbnailUploadResponse>(
    `/v1/videos/${videoId}/thumbnail`,
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
