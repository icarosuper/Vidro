// Downloads the Blender open movies once and cuts them into the clips and avatars the seed
// uploads. Everything lands in .cache/ and is reused on a rerun.
import { existsSync, mkdirSync } from 'node:fs'
import {
  CACHE_DIR,
  CLIPS,
  CLIPS_DIR,
  CREATORS,
  type Clip,
  clipPath,
  FILMS,
  SOURCES_DIR,
  UPLOAD_CLIP,
} from './catalog'
import { run } from './shell'

export function avatarPath(username: string) {
  return `${CACHE_DIR}avatars/${username}.jpg`
}

export function thumbnailPath(clip: Clip) {
  return `${CACHE_DIR}thumbnails/${clip.file.replace('.mp4', '.jpg')}`
}

function sourcePath(film: keyof typeof FILMS) {
  return `${SOURCES_DIR}${FILMS[film].fileName}`
}

async function downloadFilms() {
  mkdirSync(SOURCES_DIR, { recursive: true })
  for (const film of Object.values(FILMS)) {
    const target = `${SOURCES_DIR}${film.fileName}`
    const alreadyDownloaded = existsSync(target)
    if (alreadyDownloaded) continue

    console.log(`downloading ${film.url}`)
    await run(['curl', '-fL', '--progress-bar', '-o', `${target}.part`, film.url])
    await run(['mv', `${target}.part`, target])
  }
}

// Every clip leaves as 1280x720 H.264 + AAC, whatever the source (see Film.videoFilter), so the
// catalog grid has one aspect ratio and no black bars.
async function cutClip(clip: Clip) {
  const target = clipPath(clip)
  const alreadyCut = existsSync(target)
  if (alreadyCut) return

  console.log(`cutting ${clip.file}`)
  await run([
    'ffmpeg', '-loglevel', 'error', '-y',
    '-ss', String(clip.startSecond), '-i', sourcePath(clip.film),
    '-t', String(clip.durationSeconds),
    '-map', '0:v:0', '-map', '0:a:0',
    '-vf', `${FILMS[clip.film].videoFilter},setsar=1`,
    '-c:v', 'libx264', '-preset', 'veryfast', '-crf', '22', '-pix_fmt', 'yuv420p',
    '-c:a', 'aac', '-b:a', '128k', '-movflags', '+faststart',
    target,
  ])
}

async function cutThumbnail(clip: Clip) {
  const target = thumbnailPath(clip)
  const alreadyCut = existsSync(target)
  if (alreadyCut) return

  await run([
    'ffmpeg', '-loglevel', 'error', '-y',
    '-ss', String(clip.thumbnailAtSecond), '-i', clipPath(clip),
    '-frames:v', '1', '-q:v', '3',
    target,
  ])
}

async function cutAvatar(username: string, film: keyof typeof FILMS, second: number) {
  const target = avatarPath(username)
  const alreadyCut = existsSync(target)
  if (alreadyCut) return

  await run([
    'ffmpeg', '-loglevel', 'error', '-y',
    '-ss', String(second), '-i', sourcePath(film),
    '-frames:v', '1',
    '-vf', "crop='min(iw,ih)':'min(iw,ih)',scale=256:256",
    target,
  ])
}

export async function prepareClips() {
  await downloadFilms()

  mkdirSync(CLIPS_DIR, { recursive: true })
  mkdirSync(`${CACHE_DIR}avatars/`, { recursive: true })
  mkdirSync(`${CACHE_DIR}thumbnails/`, { recursive: true })

  for (const clip of [...CLIPS, UPLOAD_CLIP]) {
    await cutClip(clip)
    await cutThumbnail(clip)
  }
  for (const creator of CREATORS) {
    await cutAvatar(creator.username, creator.avatarFrameAt.film, creator.avatarFrameAt.second)
  }
}

if (import.meta.main) {
  await prepareClips()
  console.log(`clips ready in ${CLIPS_DIR}`)
}
