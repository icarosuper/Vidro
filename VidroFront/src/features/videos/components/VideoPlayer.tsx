import Hls from 'hls.js'
import { useEffect, useRef } from 'react'

type VideoPlayerProps = {
  src: string
  poster?: string
}

export function VideoPlayer({ src, poster }: VideoPlayerProps) {
  const videoRef = useRef<HTMLVideoElement>(null)

  useEffect(() => {
    const video = videoRef.current
    if (!video || !src) return

    const isHlsManifest = src.includes('.m3u8')

    if (isHlsManifest) {
      const browserSupportsHlsNatively =
        video.canPlayType('application/vnd.apple.mpegurl') !== ''

      if (Hls.isSupported()) {
        const hls = new Hls()
        hls.loadSource(src)
        hls.attachMedia(video)
        return () => hls.destroy()
      }

      if (browserSupportsHlsNatively) {
        video.src = src
      }

      return
    }

    video.src = src
  }, [src])

  return (
    // biome-ignore lint/a11y/useMediaCaption: no caption source exists yet (no subtitle step in the pipeline, no caption field in the API) — real gap, tracked in the root TODO.md P5
    <video
      ref={videoRef}
      poster={poster}
      controls
      className="h-full w-full rounded-lg bg-black"
    />
  )
}
