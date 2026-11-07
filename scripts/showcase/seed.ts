// Fills the stack with the showcase catalog through the public API — the same path a user takes:
// presigned PUT → MinIO webhook → Redis → worker → HMAC webhook. Safe to rerun: accounts,
// channels, videos (by title), comments and the playlist are only created when missing.
import {
  API_URL,
  AUDIENCE,
  CLIPS,
  CREATORS,
  type Clip,
  clipPath,
  VIEWER,
} from './catalog'
import { avatarPath, thumbnailPath } from './clips'

const READY_TIMEOUT_MS = 15 * 60 * 1000
const POLL_INTERVAL_MS = 2000
const VIDEO_STATUS_READY = 2
const VIDEO_STATUS_FAILED = 3
const VISIBILITY_PUBLIC = 0
const REACTION_LIKE = 1
const PLAYLIST_SCOPE_USER = 0
const PLAYLIST_VISIBILITY_PUBLIC = 0
const HTTP_CONFLICT = 409
const HTTP_TOO_MANY_REQUESTS = 429
const RATE_LIMIT_FALLBACK_SECONDS = 20

type Account = { username: string; email: string; password: string }
type Session = { username: string; token: string }
type SeededVideo = { videoId: string; clip: Clip; title: string; creator: string }

class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
  ) {
    super(message)
  }
}

async function api<T>(method: string, path: string, session: Session | null, body?: unknown) {
  const headers: Record<string, string> = { 'Content-Type': 'application/json' }
  if (session) headers.Authorization = `Bearer ${session.token}`

  const response = await fetch(`${API_URL}${path}`, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  // Auth is rate-limited per IP (RateLimit:AuthPermitLimit), and the seed signs eleven accounts
  // up and in: wait out the window instead of failing.
  const isRateLimited = response.status === HTTP_TOO_MANY_REQUESTS
  if (isRateLimited) {
    const retryAfterSeconds = Number(response.headers.get('retry-after') ?? RATE_LIMIT_FALLBACK_SECONDS)
    console.log(`  rate-limited on ${path}, waiting ${retryAfterSeconds}s`)
    await Bun.sleep(retryAfterSeconds * 1000)
    return api<T>(method, path, session, body)
  }
  if (!response.ok) {
    const responseText = await response.text()
    throw new ApiError(response.status, `${method} ${path} → ${response.status} ${responseText}`)
  }

  const hasBody = response.status !== 204 && response.headers.get('content-length') !== '0'
  if (!hasBody) return undefined as T
  const envelope = (await response.json()) as { data: T }
  return envelope.data
}

// Creating something that already exists answers 409 on a rerun; that is the expected case.
async function createUnlessExists(create: () => Promise<unknown>) {
  try {
    await create()
  } catch (error) {
    const alreadyExists = error instanceof ApiError && error.status === HTTP_CONFLICT
    if (!alreadyExists) throw error
  }
}

async function putFile(uploadUrl: string, filePath: string, contentType: string) {
  const response = await fetch(uploadUrl, {
    method: 'PUT',
    headers: { 'Content-Type': contentType },
    body: Bun.file(filePath),
  })
  if (!response.ok) throw new Error(`PUT ${filePath} → ${response.status}`)
}

async function signIn(account: Account): Promise<Session> {
  await createUnlessExists(() => api('POST', '/v1/auth/signup', null, account))
  const tokens = await api<{ accessToken: string }>('POST', '/v1/auth/signin', null, {
    email: account.email,
    password: account.password,
  })
  return { username: account.username, token: tokens.accessToken }
}

async function waitForApi() {
  const deadline = Date.now() + 120_000
  while (Date.now() < deadline) {
    const healthy = await fetch(`${API_URL}/health`).then((r) => r.ok).catch(() => false)
    if (healthy) return
    await Bun.sleep(POLL_INTERVAL_MS)
  }
  throw new Error(`API not healthy at ${API_URL} — is the stack up? (docker compose up -d)`)
}

async function setUpCreator(creator: (typeof CREATORS)[number]) {
  const session = await signIn(creator)
  await createUnlessExists(() => api('POST', '/v1/channels', session, creator.channel))

  // Both avatars are presigned PUTs; re-uploading on a rerun is harmless.
  const avatarFile = avatarPath(creator.username)
  const channelAvatar = await api<{ uploadUrl: string }>(
    'POST', `/v1/channels/${creator.channel.handle}/avatar`, session,
  )
  await putFile(channelAvatar.uploadUrl, avatarFile, 'image/jpeg')
  const userAvatar = await api<{ uploadUrl: string }>('POST', '/v1/users/me/avatar', session)
  await putFile(userAvatar.uploadUrl, avatarFile, 'image/jpeg')

  return session
}

type ChannelVideo = { videoId: string; title: string; status: { id: number } }

async function listChannelVideos(session: Session, handle: string) {
  const page = await api<{ videos: ChannelVideo[] }>(
    'GET', `/v1/users/${session.username}/channels/${handle}/videos?limit=50`, session,
  )
  return page.videos
}

async function uploadClip(session: Session, handle: string, clip: Clip) {
  const created = await api<{ videoId: string; uploadUrl: string }>(
    'POST', `/v1/users/${session.username}/channels/${handle}/videos`, session,
    { title: clip.title, description: clip.description, tags: clip.tags, visibility: VISIBILITY_PUBLIC },
  )
  await putFile(created.uploadUrl, clipPath(clip), 'video/mp4')
  console.log(`  uploaded ${clip.title}`)
  return created.videoId
}

// A video is visible only to its owner until it is Ready, so each one is polled as its creator.
async function waitUntilReady(creatorSessions: Map<string, Session>, videos: SeededVideo[]) {
  const deadline = Date.now() + READY_TIMEOUT_MS
  const pending = new Set(videos)

  while (pending.size > 0) {
    const timedOut = Date.now() > deadline
    if (timedOut) {
      const pendingTitles = [...pending].map((video) => video.title).join(', ')
      throw new Error(`still processing after 15 min: ${pendingTitles}`)
    }

    for (const video of pending) {
      const ownerSession = creatorSessions.get(video.creator)!
      const current = await api<{ status: { id: number } }>(
        'GET', `/v1/videos/${video.videoId}`, ownerSession,
      )
      if (current.status.id === VIDEO_STATUS_FAILED) throw new Error(`${video.title} is Failed`)
      if (current.status.id === VIDEO_STATUS_READY) {
        pending.delete(video)
        console.log(`  ready: ${video.title} (${pending.size} left)`)
      }
    }
    await Bun.sleep(POLL_INTERVAL_MS)
  }
}

async function seedVideos(creatorSessions: Map<string, Session>) {
  const seeded: SeededVideo[] = []

  for (const creator of CREATORS) {
    const session = creatorSessions.get(creator.username)!
    const existing = await listChannelVideos(session, creator.channel.handle)
    const creatorClips = CLIPS.filter((clip) => clip.creator === creator.username)

    for (const clip of creatorClips) {
      const existingVideo = existing.find((video) => video.title === clip.title)
      const videoId = existingVideo
        ? existingVideo.videoId
        : await uploadClip(session, creator.channel.handle, clip)
      seeded.push({ videoId, clip, title: clip.title, creator: creator.username })
    }
  }

  await waitUntilReady(creatorSessions, seeded)

  // The worker's thumbnails are taken at fixed points of the video and often land on a title
  // card; a hand-picked frame goes first on every card. Re-uploading on a rerun is harmless.
  for (const video of seeded) {
    const ownerSession = creatorSessions.get(video.creator)!
    const thumbnail = await api<{ uploadUrl: string }>(
      'POST', `/v1/videos/${video.videoId}/thumbnail`, ownerSession,
    )
    await putFile(thumbnail.uploadUrl, thumbnailPath(video.clip), 'image/jpeg')
  }
  return seeded
}

const COMMENTS = [
  'The lighting in this shot is unreal.',
  'Rendered entirely in Blender, still blows my mind.',
  'Came back to watch this again.',
  'The sound design carries this scene.',
  'Open movies like this are why I learned Blender.',
  'That transition at the end!',
]

const REPLIES = ['Agreed, especially on a big screen.', 'Same here.', 'Right?']

async function hasComments(session: Session, videoId: string) {
  const page = await api<{ comments: unknown[] }>(
    'GET', `/v1/videos/${videoId}/comments?limit=1&sort=0`, session,
  )
  return page.comments.length > 0
}

// The audience follows, watches, likes and comments with a fixed pattern per video, so
// trending has a stable order and each video page shows a different-looking thread.
async function seedEngagement(videos: SeededVideo[], audience: Session[], viewer: Session) {
  const everyone = [viewer, ...audience]

  for (const creator of CREATORS) {
    for (const session of everyone) {
      await createUnlessExists(() =>
        api('POST', `/v1/users/${creator.username}/channels/${creator.channel.handle}/follow`, session),
      )
    }
  }

  for (const [videoIndex, video] of videos.entries()) {
    const watchers = everyone.slice(0, everyone.length - (videoIndex % 4))
    for (const [watcherIndex, session] of watchers.entries()) {
      await api('POST', `/v1/videos/${video.videoId}/view`, session)
      const likes = (videoIndex + watcherIndex) % 3 !== 0
      if (likes) {
        await api('POST', `/v1/videos/${video.videoId}/react`, session, { type: REACTION_LIKE })
      }
    }

    const alreadyCommented = await hasComments(viewer, video.videoId)
    if (alreadyCommented) continue

    const commenterCount = 1 + (videoIndex % 3)
    for (let commentIndex = 0; commentIndex < commenterCount; commentIndex++) {
      const author = audience[(videoIndex + commentIndex) % audience.length]
      const content = COMMENTS[(videoIndex + commentIndex) % COMMENTS.length]
      const comment = await api<{ commentId: string }>(
        'POST', `/v1/videos/${video.videoId}/comments`, author, { content, parentCommentId: null },
      )

      const isFirstComment = commentIndex === 0
      if (!isFirstComment) continue
      const reply = REPLIES[videoIndex % REPLIES.length]
      await api('POST', `/v1/videos/${video.videoId}/comments`, viewer, {
        content: reply,
        parentCommentId: comment.commentId,
      })
      for (const liker of audience.slice(0, 3)) {
        await api('POST', `/v1/comments/${comment.commentId}/reactions`, liker, { type: REACTION_LIKE })
      }
    }
  }
}

const PLAYLIST_NAME = 'Best of the Blender open movies'

async function findOrCreatePlaylist(viewer: Session) {
  const playlists = await api<{ items: { id: string; name: string }[] }>(
    'GET', `/v1/users/${viewer.username}/playlists?limit=50`, viewer,
  )
  const existing = playlists.items.find((playlist) => playlist.name === PLAYLIST_NAME)
  if (existing) return existing.id

  const created = await api<{ playlistId: string }>('POST', '/v1/playlists', viewer, {
    name: PLAYLIST_NAME,
    description: 'One scene from each film, in release order.',
    visibility: PLAYLIST_VISIBILITY_PUBLIC,
    scope: PLAYLIST_SCOPE_USER,
    channelId: null,
  })
  return created.playlistId
}

// Items are re-added on every run: a video deleted and seeded again leaves the playlist without it.
async function seedPlaylist(viewer: Session, videos: SeededVideo[]) {
  const playlistId = await findOrCreatePlaylist(viewer)
  const picks = ['Elephants Dream — The Machine', 'Big Buck Bunny — Official Trailer',
    'Sintel — Official Trailer', 'Tears of Steel — Old Amsterdam']
  for (const title of picks) {
    const video = videos.find((candidate) => candidate.title === title)!
    await createUnlessExists(() =>
      api('POST', `/v1/playlists/${playlistId}/items`, viewer, { videoId: video.videoId }),
    )
  }
}

export async function seed() {
  await waitForApi()

  console.log('==> creators, channels and avatars')
  const creatorSessions = new Map<string, Session>()
  for (const creator of CREATORS) {
    creatorSessions.set(creator.username, await setUpCreator(creator))
  }

  console.log('==> viewer and audience')
  const viewer = await signIn(VIEWER)
  await createUnlessExists(() => api('POST', '/v1/channels', viewer, VIEWER.channel))
  const audience: Session[] = []
  for (const account of AUDIENCE) audience.push(await signIn(account))

  console.log('==> videos (upload → Processing → Ready)')
  const videos = await seedVideos(creatorSessions)

  console.log('==> follows, views, likes, comments')
  await seedEngagement(videos, audience, viewer)

  console.log('==> playlist')
  await seedPlaylist(viewer, videos)
}

if (import.meta.main) {
  await seed()
  console.log('seeded')
}
