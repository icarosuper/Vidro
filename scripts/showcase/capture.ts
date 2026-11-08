// Drives the running stack with Playwright: screenshots of the main screens (light and dark) and a
// recording of the upload flow, written to docs/media/ as WebP. Run after the seed.
import { mkdirSync, readdirSync, rmSync } from 'node:fs'
import { type Browser, type BrowserContext, chromium, type Locator, type Page } from 'playwright'
import {
  API_URL,
  CACHE_DIR,
  clipPath,
  FRONT_URL,
  GRAFANA_URL,
  MEDIA_DIR,
  UPLOAD_CLIP,
  VIEWER,
} from './catalog'
import { run } from './shell'

const RAW_DIR = `${CACHE_DIR}raw/`
const RECORDING_DIR = `${CACHE_DIR}recording/`
const DESKTOP = { width: 1440, height: 900 }
const MOBILE = { width: 390, height: 844 }
const RECORDING_SIZE = { width: 1280, height: 720 }
const PROCESSING_TIMEOUT_MS = 5 * 60 * 1000
// Time the recording lingers on a screen that is a result — the feed, "Upload complete!".
const HOLD_ON_SCREEN_MS = 2500
const PLAYBACK_SHOWN_MS = 6000
// Sintel's face, in the trailer the watch page shows.
const WATCH_FRAME_SECOND = 30
// Tall enough for the player, the title row and the first comments.
const WATCH_SHOT_HEIGHT = 1500

type Theme = 'light' | 'dark'

type VideoSummary = { videoId: string; title: string }

async function apiGet<T>(path: string, token?: string) {
  const headers: Record<string, string> = token ? { Authorization: `Bearer ${token}` } : {}
  const response = await fetch(`${API_URL}${path}`, { headers })
  if (!response.ok) throw new Error(`GET ${path} → ${response.status}`)
  const envelope = (await response.json()) as { data: T }
  return envelope.data
}

async function findVideoId(title: string) {
  const found = await apiGet<{ videos: VideoSummary[] }>(
    `/v1/videos/search?q=${encodeURIComponent(title)}&limit=20`,
  )
  const match = found.videos.find((video) => video.title === title)
  if (!match) throw new Error(`"${title}" not found — run the seed first`)
  return match.videoId
}

async function findPlaylistId() {
  const playlists = await apiGet<{ items: { id: string }[] }>(`/v1/users/${VIEWER.username}/playlists?limit=50`)
  const firstPlaylist = playlists.items[0]
  if (!firstPlaylist) throw new Error('no playlist — run the seed first')
  return firstPlaylist.id
}

// ─── Browser helpers ─────────────────────────────────────────────────────────

async function newContext(browser: Browser, theme: Theme, options: Parameters<Browser['newContext']>[0] = {}) {
  const context = await browser.newContext({
    viewport: DESKTOP,
    deviceScaleFactor: 2,
    colorScheme: theme,
    ...options,
  })
  // ThemeToggle reads localStorage before the first paint.
  await context.addInitScript((mode) => window.localStorage.setItem('theme', mode), theme)
  return context
}

// How fast the browser acts. Screenshots only need the end state; the recording is watched by
// people, so it types at reading speed and lets each screen sit before and after every action.
type Pace = {
  typingDelayMs: number
  pauseBeforeActionMs: number
  pauseAfterActionMs: number
  // Steps of the mouse glide to a button; 1 jumps straight there.
  mouseSteps: number
}

const SCREENSHOT_PACE: Pace = { typingDelayMs: 0, pauseBeforeActionMs: 0, pauseAfterActionMs: 0, mouseSteps: 1 }
const RECORDING_PACE: Pace = { typingDelayMs: 90, pauseBeforeActionMs: 700, pauseAfterActionMs: 1200, mouseSteps: 30 }

// Moves the mouse to the element before clicking it — with the recording's cursor overlay, the
// viewer sees where the click is going instead of the screen just changing.
async function clickPaced(page: Page, target: Locator, pace: Pace) {
  const box = await target.boundingBox()
  if (box) {
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2, { steps: pace.mouseSteps })
  }
  await page.waitForTimeout(pace.pauseBeforeActionMs)
  await target.click()
  await page.waitForTimeout(pace.pauseAfterActionMs)
}

async function typePaced(page: Page, target: Locator, text: string, pace: Pace) {
  await clickPaced(page, target, { ...pace, pauseAfterActionMs: 0 })
  await target.pressSequentially(text, { delay: pace.typingDelayMs })
  await page.waitForTimeout(pace.pauseAfterActionMs)
}

async function signIn(page: Page, pace: Pace = SCREENSHOT_PACE) {
  await page.goto(FRONT_URL)
  await waitForImages(page)
  await page.waitForTimeout(pace.pauseAfterActionMs)
  await clickPaced(page, page.getByRole('button', { name: 'Sign in' }), pace)
  const dialog = page.getByRole('dialog')
  await typePaced(page, dialog.getByPlaceholder('you@example.com'), VIEWER.email, pace)
  await typePaced(page, dialog.getByPlaceholder('••••••••'), VIEWER.password, pace)
  await clickPaced(page, dialog.getByRole('button', { name: 'Sign in' }), pace)
  await dialog.waitFor({ state: 'hidden' })
}

// Headless Chromium records no mouse pointer. This draws one that follows the mouse and pulses on
// each click, so the recording shows what was clicked.
function showCursorOverlay() {
  const install = () => {
    const cursor = document.createElement('div')
    cursor.style.cssText = [
      'position:fixed', 'z-index:2147483647', 'pointer-events:none', 'left:0', 'top:0',
      'width:22px', 'height:22px', 'margin:-11px 0 0 -11px', 'border-radius:50%',
      'background:rgba(255,255,255,0.35)', 'border:2px solid rgba(255,255,255,0.9)',
      'box-shadow:0 0 0 1px rgba(0,0,0,0.5)', 'transition:transform 120ms ease-out',
    ].join(';')
    document.body.appendChild(cursor)
    document.addEventListener('mousemove', (event) => {
      cursor.style.left = `${event.clientX}px`
      cursor.style.top = `${event.clientY}px`
    }, true)
    document.addEventListener('mousedown', () => { cursor.style.transform = 'scale(0.6)' }, true)
    document.addEventListener('mouseup', () => { cursor.style.transform = 'scale(1)' }, true)
  }
  if (document.body) install()
  else document.addEventListener('DOMContentLoaded', install)
}

// Network idle is not enough: thumbnails decode after their request ends.
async function waitForImages(page: Page) {
  await page.waitForLoadState('networkidle')
  await page.waitForFunction(() =>
    Array.from(document.images).every((image) => image.complete && image.naturalWidth > 0),
  )
}

// Freezes the player on a chosen frame, so the screenshot shows the film rather than a black box.
// A seek alone leaves Chrome's buffering spinner over the picture; playing a moment and pausing
// clears it.
async function freezePlayerAt(page: Page, second: number) {
  const player = page.locator('video').first()
  await player.waitFor()
  await player.evaluate(async (video: HTMLVideoElement, targetSecond) => {
    video.muted = true
    video.currentTime = targetSecond
    await video.play()
    await new Promise((resolve) => setTimeout(resolve, 400))
    video.pause()
  }, second)
}

async function toWebp(rawPng: string, name: string) {
  await run([
    'ffmpeg', '-loglevel', 'error', '-y', '-i', rawPng,
    '-c:v', 'libwebp', '-quality', '82', '-compression_level', '6',
    `${MEDIA_DIR}${name}.webp`,
  ])
}

const HOVER_FADE_MS = 600

// `height` grows the shot past the viewport, for pages whose point is below the fold.
async function screenshot(page: Page, name: string, height?: number) {
  const rawPng = `${RAW_DIR}${name}.png`
  const viewport = page.viewportSize()!
  const clip = height ? { x: 0, y: 0, width: viewport.width, height } : undefined
  // Off every card: the pointer is left where the last click was (often the sign-in dialog, over
  // the home's featured card), and a hovered card shows its tags and lifted surface.
  await page.mouse.move(viewport.width / 2, 1)
  // The hover styles fade out (CSS transitions); shooting right after the move catches them.
  await page.waitForTimeout(HOVER_FADE_MS)
  await page.screenshot({ path: rawPng, fullPage: Boolean(height), clip })
  await toWebp(rawPng, name)
  console.log(`  ${name}.webp`)
}

// ─── Screens ─────────────────────────────────────────────────────────────────

type Targets = { watchVideoId: string; playlistId: string }

async function captureScreens(browser: Browser, theme: Theme, targets: Targets) {
  const context = await newContext(browser, theme)
  const page = await context.newPage()
  await signIn(page)
  await waitForImages(page)
  await screenshot(page, `home-${theme}`)

  await page.goto(`${FRONT_URL}/watch/${targets.watchVideoId}`)
  await freezePlayerAt(page, WATCH_FRAME_SECOND)
  await waitForImages(page)
  await screenshot(page, `watch-${theme}`, WATCH_SHOT_HEIGHT)

  await page.goto(`${FRONT_URL}/mango/tears-of-steel`)
  await waitForImages(page)
  await screenshot(page, `channel-${theme}`)

  await page.goto(`${FRONT_URL}/search?q=dream`)
  await waitForImages(page)
  await screenshot(page, `search-${theme}`)

  await page.goto(`${FRONT_URL}/playlists/${targets.playlistId}`)
  await waitForImages(page)
  await screenshot(page, `playlist-${theme}`)

  await context.close()
}

async function captureMobile(browser: Browser, targets: Targets) {
  const context = await newContext(browser, 'dark', { viewport: MOBILE, deviceScaleFactor: 3, isMobile: true, hasTouch: true })
  const page = await context.newPage()
  await page.goto(FRONT_URL)
  await waitForImages(page)
  await screenshot(page, 'mobile-home')

  await page.goto(`${FRONT_URL}/watch/${targets.watchVideoId}`)
  await freezePlayerAt(page, WATCH_FRAME_SECOND)
  await waitForImages(page)
  await screenshot(page, 'mobile-watch')
  await context.close()
}

async function captureGrafana(browser: Browser) {
  // Basic auth instead of the login form: Grafana accepts it, and it skips the first-login
  // "change your password" step. Sent up front, because Grafana answers a missing login with a
  // redirect to /login, not the 401 that would make the browser offer credentials.
  const basicAuth = Buffer.from('admin:admin').toString('base64')
  const context = await newContext(browser, 'dark', {
    extraHTTPHeaders: { Authorization: `Basic ${basicAuth}` },
  })
  const page = await context.newPage()
  await page.goto(`${GRAFANA_URL}/d/video-processor?orgId=1&from=now-1h&to=now&kiosk`)
  await page.waitForLoadState('networkidle')
  // Panels draw after their queries resolve, with no event to wait on.
  await page.waitForTimeout(3000)
  await screenshot(page, 'grafana')
  await context.close()
}

// ─── Upload recording ────────────────────────────────────────────────────────

async function recordUploadFlow(browser: Browser) {
  rmSync(RECORDING_DIR, { recursive: true, force: true })
  const context: BrowserContext = await newContext(browser, 'dark', {
    viewport: RECORDING_SIZE,
    deviceScaleFactor: 1,
    recordVideo: { dir: RECORDING_DIR, size: RECORDING_SIZE },
  })
  await context.addInitScript(showCursorOverlay)
  const pace = RECORDING_PACE
  const page = await context.newPage()
  await signIn(page, pace)
  await waitForImages(page)
  await page.waitForTimeout(HOLD_ON_SCREEN_MS)

  await clickPaced(page, page.getByRole('link', { name: 'Upload' }), pace)
  // The file picker is a native dialog that recordings cannot show; the file is set directly
  // and the pause lets its name appear next to the button before typing starts.
  await page.locator('input[type=file][accept="video/*"]').setInputFiles(clipPath(UPLOAD_CLIP))
  await page.waitForTimeout(pace.pauseAfterActionMs)
  await typePaced(page, page.getByLabel('Title'), UPLOAD_CLIP.title, pace)
  await typePaced(page, page.getByLabel('Description'), UPLOAD_CLIP.description, pace)
  await typePaced(page, page.getByLabel('Tags'), UPLOAD_CLIP.tags.join(', '), pace)
  await clickPaced(page, page.getByRole('button', { name: 'Upload video' }), pace)

  await page.getByText('Upload complete!').waitFor({ timeout: PROCESSING_TIMEOUT_MS })
  await page.waitForTimeout(HOLD_ON_SCREEN_MS)
  await clickPaced(page, page.getByRole('link', { name: 'Watch your video' }), pace)

  const player = page.locator('video').first()
  await player.waitFor()
  await player.evaluate((video: HTMLVideoElement) => {
    video.muted = true
    return video.play()
  })
  await page.waitForTimeout(PLAYBACK_SHOWN_MS)

  await context.close()
  const recordedFile = readdirSync(RECORDING_DIR).find((file) => file.endsWith('.webm'))!
  await run([
    'ffmpeg', '-loglevel', 'error', '-y', '-i', `${RECORDING_DIR}${recordedFile}`,
    '-vf', 'fps=15,scale=960:-1:flags=lanczos',
    '-c:v', 'libwebp_anim', '-loop', '0', '-quality', '70', '-compression_level', '6',
    `${MEDIA_DIR}upload-flow.webp`,
  ])
  console.log('  upload-flow.webp')
}

// The recording uploads the same clip every run; drop the previous copies so the catalog does
// not fill up with them. `make demo` uploads its test card to the same channel too, and those
// fill the recording's "Up next" (the channel's own videos come first) — they go as well.
const DEMO_SCRIPT_VIDEO_TITLE = 'Demo video'

async function deletePreviousUploads() {
  const signInResponse = await fetch(`${API_URL}/v1/auth/signin`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: VIEWER.email, password: VIEWER.password }),
  })
  const { data } = (await signInResponse.json()) as { data: { accessToken: string } }
  const channelVideos = await apiGet<{ videos: VideoSummary[] }>(
    `/v1/users/${VIEWER.username}/channels/${VIEWER.channel.handle}/videos?limit=50`,
    data.accessToken,
  )
  const previousUploads = channelVideos.videos.filter(
    (video) => video.title === UPLOAD_CLIP.title || video.title === DEMO_SCRIPT_VIDEO_TITLE,
  )
  for (const video of previousUploads) {
    await fetch(`${API_URL}/v1/videos/${video.videoId}`, {
      method: 'DELETE',
      headers: { Authorization: `Bearer ${data.accessToken}` },
    })
  }
}

export async function capture() {
  mkdirSync(RAW_DIR, { recursive: true })
  mkdirSync(MEDIA_DIR, { recursive: true })

  const targets: Targets = {
    watchVideoId: await findVideoId('Sintel — Official Trailer'),
    playlistId: await findPlaylistId(),
  }

  const browser = await chromium.launch()
  try {
    console.log('==> screens')
    await captureScreens(browser, 'light', targets)
    await captureScreens(browser, 'dark', targets)
    await captureMobile(browser, targets)
    await captureGrafana(browser)

    console.log('==> upload recording')
    await deletePreviousUploads()
    await recordUploadFlow(browser)
  } finally {
    await browser.close()
  }
}

if (import.meta.main) {
  await capture()
  console.log(`media written to ${MEDIA_DIR}`)
}
