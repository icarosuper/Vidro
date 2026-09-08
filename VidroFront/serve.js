// Production entry: dist/server/server.js only renders SSR — it never serves the built client
// assets (in dev/preview Vite's own static middleware does that). Running it directly makes
// every /assets/*.css and *.js request fall through to the catch-all route and answer HTML,
// which is why the page rendered unstyled inside Docker.
import ssr from './dist/server/server.js'

const clientDir = `${import.meta.dir}/dist/client`

Bun.serve({
  port: Number(process.env.PORT ?? 3000),
  async fetch(request) {
    const { pathname } = new URL(request.url)
    const asset = Bun.file(`${clientDir}${pathname}`)
    const isStaticAsset = pathname !== '/' && (await asset.exists())

    if (isStaticAsset) {
      // Vite hashes filenames under /assets, so those are safe to cache forever.
      const isHashedBundle = pathname.startsWith('/assets/')
      return new Response(asset, {
        headers: isHashedBundle
          ? { 'Cache-Control': 'public, max-age=31536000, immutable' }
          : {},
      })
    }

    return ssr.fetch(request)
  },
})
