import {
  CORRELATION_ID_HEADER,
  newCorrelationId,
} from '#/shared/lib/correlation-id'
import type { Playlist } from './types'

const API_URL = process.env.VITE_API_URL ?? 'http://localhost:5000'

// The SSR loader's fetch, with the token from the router context: apiClient has no token on the
// server, so through it the owner would get `isOwner: false` (and a 404 on a private playlist)
// until the client refetched. Same shape as fetchVideoSsr.
export async function fetchPlaylistSsr(
  playlistId: string,
  accessToken: string | null,
): Promise<Playlist> {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    [CORRELATION_ID_HEADER]: newCorrelationId(),
  }
  if (accessToken) {
    headers.Authorization = `Bearer ${accessToken}`
  }

  const response = await fetch(`${API_URL}/v1/playlists/${playlistId}`, {
    headers,
  })
  const data = await response.json()

  if (!response.ok) {
    throw new Error(data.message ?? 'Failed to fetch playlist')
  }

  return (data as { data: Playlist }).data
}
