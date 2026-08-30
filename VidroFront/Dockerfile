FROM oven/bun:1 AS build
WORKDIR /app

COPY package.json bun.lock ./
RUN bun install --frozen-lockfile

COPY . .
# Vite bakes import.meta.env at build time, so this is the URL the BROWSER will use.
# The SSR server reads process.env.VITE_API_URL at runtime instead (see api-client.ts).
ARG VITE_API_URL=http://localhost:5000
ENV VITE_API_URL=$VITE_API_URL
RUN bun run build

FROM oven/bun:1
WORKDIR /app
# The server bundle keeps its dependencies external (h3, etc.), so it needs node_modules.
COPY --from=build /app/node_modules ./node_modules
COPY --from=build /app/dist ./dist
EXPOSE 3000
ENV PORT=3000
# dist/server/server.js default-exports a { fetch } handler; bun serves it directly.
CMD ["bun", "dist/server/server.js"]
