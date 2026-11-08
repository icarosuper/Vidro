# Showcase

Regenerates the screenshots and the upload recording in [`docs/media/`](../../docs/media/) that the
root README shows. Rerun it whenever the UI changes.

```bash
make showcase
```

Needs docker, [bun](https://bun.sh) and ffmpeg on the host. The first run downloads about 500 MB of
Blender open movies into `.cache/` (gitignored) and reuses them after that.

## What it does

| Step | File | What happens |
|---|---|---|
| Clips | [`clips.ts`](clips.ts) | Downloads the films, cuts the clips at 1280x720 (scope films cropped to 16:9, not letterboxed), and cuts the avatars and custom thumbnails |
| Seed | [`seed.ts`](seed.ts) | Through the public API: four creators with a channel each, the viewer `demo@example.com` (the same user as `make demo`), an audience of six, the ten clips (presigned PUT → worker → `Ready`), custom thumbnails, follows, views, likes, comment threads and a playlist |
| Capture | [`capture.ts`](capture.ts) | Playwright (Chromium): home, watch, channel, search and playlist in light and dark, two mobile shots, the worker's Grafana dashboard, and a recording of the upload flow, all converted to WebP |

The catalog itself (films, clips, accounts, what each frame shows) is in
[`catalog.ts`](catalog.ts). Each step runs on its own too: `bun run clips`, `bun run seed`,
`bun run capture`.

The recording runs at reading speed — typing, pauses around each click, and how long it lingers on
"Upload complete!" and the player are `RECORDING_PACE`, `HOLD_ON_SCREEN_MS` and `PLAYBACK_SHOWN_MS`
at the top of `capture.ts`. Headless Chromium records no mouse pointer, so the page draws one
(`showCursorOverlay`) that glides to each target and pulses on the click.

The seed is safe to rerun: accounts, channels and videos (matched by title) are only created when
they are missing. The recording uploads a new video on every run and deletes the copy the previous
run left, along with the "Demo video"s `make demo` uploads to the same channel (they would fill the
recording's "Up next"). Each screenshot is taken with the pointer moved off the page's cards.

## Gotchas

- **Auth is rate-limited** (10 requests per minute per IP, `RateLimit:AuthPermitLimit`). The seed
  signs up and signs in eleven accounts, so the first run spends about two minutes waiting out `429`s.
- **Host port taken** (5432, 5000, 3000…): see "Run" in the root README.
- **Replacing a clip** (`catalog.ts`): delete its video, or the seed keeps the old one with the same
  title.

## Credits

All footage is from the Blender Foundation open movies, under Creative Commons Attribution:
Elephants Dream (CC BY 2.5), Big Buck Bunny, Sintel and Tears of Steel (CC BY 3.0). Each channel and
video description carries the credit line.
