import viteReact from '@vitejs/plugin-react'
import tsconfigPaths from 'vite-tsconfig-paths'
import { defineConfig } from 'vitest/config'

// Separate from vite.config.ts on purpose: the TanStack Start plugin resolves React through the
// `react-server` condition, and under Vitest that hands the tests a React whose hook dispatcher is
// null — every render dies on `useState`. The tests need no SSR pipeline, only the alias and JSX.
export default defineConfig({
  plugins: [tsconfigPaths({ projects: ['./tsconfig.json'] }), viteReact()],
})
