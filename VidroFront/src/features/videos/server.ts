import type { Video } from './types'

const API_URL = process.env.VITE_API_URL ?? 'http://localhost:5000'

export async function fetchVideoSsr(videoId: string, accessToken: string | null): Promise<Video> {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
  }
  if (accessToken) {
    headers['Authorization'] = `Bearer ${accessToken}`
  }

  const response = await fetch(`${API_URL}/v1/videos/${videoId}`, { headers })
  const data = await response.json()

  if (!response.ok) {
    throw new Error(data.message ?? 'Failed to fetch video')
  }

  return (data as { data: Video }).data
}
