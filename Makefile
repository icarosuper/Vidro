.PHONY: demo showcase

# Stack up, a generated video through the whole pipeline, player URL printed. See scripts/demo.sh.
demo:
	./scripts/demo.sh

# Stack up, the Blender open-movie catalog seeded through the API, screenshots and the upload
# recording written to docs/media/. See scripts/showcase/README.md.
showcase:
	docker compose up -d --build
	docker compose wait minio-init
	cd scripts/showcase && bun install --frozen-lockfile && bunx playwright install chromium && bun run all
