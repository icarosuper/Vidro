import Hls from 'hls.js'
import { useEffect, useRef } from 'react'

type VideoPlayerProps = {
  src: string
  poster?: string
  // Fired when playback reaches the end — a playlist moves on to its next video.
  onEnded?: () => void
  // Starts on its own — set while playing a playlist, where the viewer already chose to play.
  // Browsers allow it after a click in the page, which is how a playlist is always reached.
  autoPlay?: boolean
}

export function VideoPlayer({
  src,
  poster,
  onEnded,
  autoPlay = false,
}: VideoPlayerProps) {
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
    // biome-ignore lint/a11y/useMediaCaption: no caption source exists yet (no subtitle step in the pipeline, no caption field in the API) — real gap, tracked in docs/TODO.md P5
    <video
      ref={videoRef}
      poster={poster}
      onEnded={onEnded}
      autoPlay={autoPlay}
      controls
      className="h-full w-full bg-black"
    />
  )
}
