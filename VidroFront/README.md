# VidroFront

Frontend for Vidro, a video platform.
Consumes [VidroApi](https://github.com/icarosuper/VidroApi); videos processed by [VidroProcessor](https://github.com/icarosuper/video-processor-go).
Built with TanStack Start (SSR) + React.

## Stack

- **TanStack Start** — SSR framework with file-based routing
- **TanStack Query** — server state management
- **shadcn/ui** — component library
- **Tailwind CSS v4** — styling
- **Bun** — runtime and package manager
- **Biome** — linting and formatting
- **Vitest** — testing

## Quick Start

```bash
cp .env.example .env
bun install
bun run dev
```

Default: `http://localhost:3000`

Requires VidroApi running at `http://localhost:5000` (set via `VITE_API_URL`).

## Commands

```bash
bun run dev       # dev server
bun run build     # production build
bun run test      # all tests
bun run lint      # biome lint
bun run format    # biome format
bun run check     # biome check (lint + format)
```

## Project Structure

```
src/
├── features/           # Feature modules
│   ├── auth/
│   ├── channels/
│   ├── comments/
│   ├── playlists/
│   ├── users/
│   └── videos/
├── shared/
│   └── lib/
│       └── api-client.ts   # All HTTP calls go through here
├── components/         # Shared UI components
├── routes/             # File-based routes (TanStack Router)
└── styles.css
```

## Environment

```bash
VITE_API_URL=http://localhost:5000
```

