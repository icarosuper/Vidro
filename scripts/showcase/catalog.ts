// What the showcase catalog holds: the Blender open movies, cut into clips and spread over one
// channel per film. Everything here is CC-BY — the credit goes in each channel and video
// description and in the root README.

export const API_URL = 'http://localhost:5000'
export const FRONT_URL = 'http://localhost:3000'
export const GRAFANA_URL = 'http://localhost:3001'

export const CACHE_DIR = new URL('./.cache/', import.meta.url).pathname
export const SOURCES_DIR = `${CACHE_DIR}sources/`
export const CLIPS_DIR = `${CACHE_DIR}clips/`
export const MEDIA_DIR = new URL('../../docs/media/', import.meta.url).pathname

export type Film = {
  fileName: string
  url: string
  credit: string
  // FFmpeg filter that fills 1280x720 with picture: the scope films are cropped to 16:9 instead
  // of letterboxed, so no thumbnail is half black bars.
  videoFilter: string
}

const FILL_16_9 = 'scale=-2:720,crop=1280:720'

export const FILMS: Record<string, Film> = {
  tearsOfSteel: {
    fileName: 'tears_of_steel_720p.mov',
    url: 'https://download.blender.org/demo/movies/ToS/tears_of_steel_720p.mov',
    credit: 'Tears of Steel — (CC) Blender Foundation | mango.blender.org — CC BY 3.0',
    videoFilter: FILL_16_9,
  },
  elephantsDream: {
    fileName: 'elephantsdream-480-h264-st-aac.mov',
    url: 'https://download.blender.org/ED/elephantsdream-480-h264-st-aac.mov',
    credit: 'Elephants Dream — (CC) Blender Foundation | orange.blender.org — CC BY 2.5',
    videoFilter: 'scale=1280:720',
  },
  sintel: {
    fileName: 'sintel_trailer-720p.mp4',
    url: 'https://download.blender.org/durian/trailer/sintel_trailer-720p.mp4',
    credit: 'Sintel — (CC) Blender Foundation | durian.blender.org — CC BY 3.0',
    // The trailer ships with the letterbox baked in (cropdetect: 1280x544 at y=88).
    videoFilter: `crop=1280:544:0:88,${FILL_16_9}`,
  },
  bigBuckBunny: {
    fileName: 'trailer_480p.mov',
    url: 'https://download.blender.org/peach/trailer/trailer_480p.mov',
    credit: 'Big Buck Bunny — (CC) Blender Foundation | peach.blender.org — CC BY 3.0',
    videoFilter: 'scale=1280:720',
  },
}

export type Creator = {
  username: string
  email: string
  password: string
  channel: { handle: string; name: string; description: string }
  // Second, in the source film, of the frame cropped into the channel avatar.
  avatarFrameAt: { film: keyof typeof FILMS; second: number }
}

const PASSWORD = 'showcase-password'

export const CREATORS: Creator[] = [
  {
    username: 'mango',
    email: 'mango@example.com',
    password: PASSWORD,
    channel: {
      handle: 'tears-of-steel',
      name: 'Tears of Steel',
      description: `Scenes from the fourth Blender open movie: sci-fi, live action and VFX in Amsterdam.\n\n${FILMS.tearsOfSteel.credit}`,
    },
    avatarFrameAt: { film: 'tearsOfSteel', second: 300 },
  },
  {
    username: 'orange',
    email: 'orange@example.com',
    password: PASSWORD,
    channel: {
      handle: 'elephants-dream',
      name: 'Elephants Dream',
      description: `Scenes from the first Blender open movie.\n\n${FILMS.elephantsDream.credit}`,
    },
    avatarFrameAt: { film: 'elephantsDream', second: 340 },
  },
  {
    username: 'durian',
    email: 'durian@example.com',
    password: PASSWORD,
    channel: {
      handle: 'sintel',
      name: 'Sintel',
      description: `A girl, a baby dragon, and a long search.\n\n${FILMS.sintel.credit}`,
    },
    avatarFrameAt: { film: 'sintel', second: 30 },
  },
  {
    username: 'peach',
    email: 'peach@example.com',
    password: PASSWORD,
    channel: {
      handle: 'big-buck-bunny',
      name: 'Big Buck Bunny',
      description: `A big rabbit, three bullies, and payback.\n\n${FILMS.bigBuckBunny.credit}`,
    },
    avatarFrameAt: { film: 'bigBuckBunny', second: 16 },
  },
]

export type Clip = {
  file: string
  film: keyof typeof FILMS
  startSecond: number
  durationSeconds: number
  // Second, inside the clip, of the frame uploaded as the custom thumbnail.
  thumbnailAtSecond: number
  creator: string
  title: string
  description: string
  tags: string[]
}

export const CLIPS: Clip[] = [
  {
    file: 'tos-the-bridge.mp4',
    film: 'tearsOfSteel',
    startSecond: 15,
    durationSeconds: 40,
    thumbnailAtSecond: 15,
    creator: 'mango',
    title: 'Tears of Steel — The Bridge',
    description: 'Thom and Celia, years before the robots.',
    tags: ['sci-fi', 'live action', 'blender'],
  },
  {
    file: 'tos-old-amsterdam.mp4',
    film: 'tearsOfSteel',
    startSecond: 60,
    durationSeconds: 45,
    thumbnailAtSecond: 21,
    creator: 'mango',
    title: 'Tears of Steel — Old Amsterdam',
    description: 'The city under siege, seen from the dome.',
    tags: ['sci-fi', 'vfx', 'blender'],
  },
  {
    file: 'tos-the-robot-hand.mp4',
    film: 'tearsOfSteel',
    startSecond: 350,
    durationSeconds: 45,
    thumbnailAtSecond: 42,
    creator: 'mango',
    title: 'Tears of Steel — The Robot Hand',
    description: 'The plan to rewrite the past starts on the same bridge.',
    tags: ['sci-fi', 'robots', 'blender'],
  },
  {
    file: 'tos-last-stand.mp4',
    film: 'tearsOfSteel',
    startSecond: 450,
    durationSeconds: 50,
    thumbnailAtSecond: 27,
    creator: 'mango',
    title: 'Tears of Steel — Last Stand',
    description: 'Everything goes wrong at once.',
    tags: ['sci-fi', 'action', 'blender'],
  },
  {
    file: 'ed-the-machine.mp4',
    film: 'elephantsDream',
    startSecond: 15,
    durationSeconds: 40,
    thumbnailAtSecond: 24,
    creator: 'orange',
    title: 'Elephants Dream — The Machine',
    description: 'Proog shows Emo around a world of wires and switches.',
    tags: ['animation', 'surreal', 'blender'],
  },
  {
    file: 'ed-proogs-tour.mp4',
    film: 'elephantsDream',
    startSecond: 240,
    durationSeconds: 45,
    thumbnailAtSecond: 21,
    creator: 'orange',
    title: "Elephants Dream — Proog's Tour",
    description: 'Do not touch anything.',
    tags: ['animation', 'surreal', 'blender'],
  },
  {
    file: 'ed-the-telephone.mp4',
    film: 'elephantsDream',
    startSecond: 380,
    durationSeconds: 40,
    thumbnailAtSecond: 30,
    creator: 'orange',
    title: 'Elephants Dream — The Telephone',
    description: 'Some doors are better left closed.',
    tags: ['animation', 'blender'],
  },
  {
    file: 'ed-into-the-light.mp4',
    film: 'elephantsDream',
    startSecond: 480,
    durationSeconds: 45,
    thumbnailAtSecond: 15,
    creator: 'orange',
    title: 'Elephants Dream — Into the Light',
    description: 'The machine finally answers back.',
    tags: ['animation', 'surreal', 'blender'],
  },
  {
    file: 'sintel-trailer.mp4',
    film: 'sintel',
    startSecond: 0,
    durationSeconds: 52,
    thumbnailAtSecond: 30,
    creator: 'durian',
    title: 'Sintel — Official Trailer',
    description: 'The third Blender open movie.',
    tags: ['animation', 'fantasy', 'trailer', 'blender'],
  },
  {
    file: 'bbb-trailer.mp4',
    film: 'bigBuckBunny',
    startSecond: 0,
    durationSeconds: 33,
    thumbnailAtSecond: 11,
    creator: 'peach',
    title: 'Big Buck Bunny — Official Trailer',
    description: 'The second Blender open movie.',
    tags: ['animation', 'comedy', 'trailer', 'blender'],
  },
]

// The clip uploaded live in the recorded upload flow — kept out of the seeded catalog so the
// recording ends with a new video, and short so the Processing wait stays short.
export const UPLOAD_CLIP: Clip = {
  file: 'sintel-dragon.mp4',
  film: 'sintel',
  startSecond: 20,
  durationSeconds: 15,
  thumbnailAtSecond: 6,
  creator: 'demo',
  title: 'Sintel — The Dragon',
  description: 'Uploaded live by the showcase recording.',
  tags: ['animation', 'blender'],
}

// The signed-in viewer in every screenshot: the same user `make demo` creates.
export const VIEWER = {
  username: 'demo',
  email: 'demo@example.com',
  password: 'demo-password',
  channel: { handle: 'demo-channel', name: 'Demo channel', description: null },
}

// Extra accounts that only watch, like and comment, so counts and threads look lived-in. Views
// are deduplicated per user, so each one adds at most one view per video.
export const AUDIENCE = ['ana_lima', 'kenji', 'marta_v', 'oskar', 'priya', 'tomas'].map(
  (username) => ({ username, email: `${username}@example.com`, password: PASSWORD }),
)

export function clipPath(clip: Clip) {
  return `${CLIPS_DIR}${clip.file}`
}
