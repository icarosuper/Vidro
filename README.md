# Vidro

Plataforma de vídeo — upload, processamento assíncrono e reprodução. **Monorepo** com três serviços
que sobem juntos e versionam juntos.

| Serviço | O que é | Stack |
|---|---|---|
| [`VidroApi/`](VidroApi/) | API REST: auth, canais, vídeos, comentários, playlists | .NET 10, Clean Architecture + Vertical Slice, Postgres, EF Core |
| [`VidroFront/`](VidroFront/) | Interface web, SSR seletivo | TanStack Start + React, shadcn/ui, Tailwind v4, bun |
| [`VidroProcessor/`](VidroProcessor/) | Worker que transcodifica: fila Redis → pipeline FFmpeg de 7 passos → MinIO → webhook | Go |

Infra compartilhada: **Postgres** (dados), **Redis** (fila de jobs), **MinIO** (objetos),
**Prometheus + Grafana + Loki** (observabilidade).

## Subir tudo

```bash
docker compose up -d --build
```

| | URL | Credencial |
|---|---|---|
| Front | http://localhost:3000 | — |
| API | http://localhost:5000 (`/health`, `/openapi/v1.json`) | — |
| Grafana | http://localhost:3001 | `admin` / `admin` |
| MinIO console | http://localhost:9001 | `minioadmin` / `minioadmin` |
| Prometheus | http://localhost:9090 | — |

Credenciais de desenvolvimento. A API aplica as migrations do EF no startup, então o Postgres pode
subir vazio. Para trabalhar no front, prefira `bun run dev` no host (HMR) contra este stack.

Para rodar um serviço isolado contra a infra do compose, veja o README de cada um.

## O fluxo que atravessa os três

```
browser ──1──▶ API ──2──▶ MinIO ──3──▶ API ──4──▶ Redis ──5──▶ Processor
                                                                   │
   player ◀──8── API ◀──7── webhook ◀────────── MinIO ◀──6─────────┘
```

1. Front pede uma URL de upload; a API cria o vídeo em `PendingUpload`.
2. Browser sobe o arquivo direto para o MinIO por **presigned PUT** (`raw/<videoId>`).
3. MinIO dispara a notificação de `put` para a API (`/webhooks/minio-upload-completed`).
4. A API publica o `videoId` na fila Redis (`video_queue`) e marca `Processing`.
5. O Processor consome com `BRPOPLPUSH` (lease em `video_queue:processing`).
6. Pipeline FFmpeg: validate → analyze → transcode → thumbnails → áudio → preview → HLS. Artefatos
   sobem para o MinIO.
7. O worker chama `/webhooks/video-processed` (HMAC-SHA256) com os caminhos gerados.
8. A API marca `Ready`; o front toca o MP4 processado (o player já suporta HLS — falta o campo na
   API, ver P6 no `TODO.md`).

Falhou no meio? O runbook é [`docs/troubleshooting-stuck-video.md`](docs/troubleshooting-stuck-video.md).

## Documentação

| Arquivo | Para quê |
|---|---|
| [`CLAUDE.md`](CLAUDE.md) | Regras que valem para os três serviços: idioma, commits, escopo, contrato compartilhado |
| [`MONOREPO.md`](MONOREPO.md) | Por que é monorepo, o que foi decidido e o que isso custou |
| [`TODO.md`](TODO.md) | Backlog dos três serviços, com referência de arquivo/linha |
| [`docs/troubleshooting-stuck-video.md`](docs/troubleshooting-stuck-video.md) | Vídeo preso em `Processing` — atravessa API, Redis, worker e MinIO |

Cada serviço tem o próprio `CLAUDE.md` e `docs/agents/` com o que é específico dele.

## Testes

```bash
cd VidroApi       && dotnet test
cd VidroFront     && bun run test
cd VidroProcessor && go test ./...
```

CI: `.github/workflows/{api,front,processor}.yml`, um por serviço, com filtro de path — mexer no
front não dispara build de .NET. Não há pipeline de deploy; deploy é manual.

## Convenções

Commits em **português**, Conventional Commits, só o assunto. Padrão é commitar direto na `master`;
branch só para feature grande. Mudança no **contrato compartilhado** (nome de fila, caminho no MinIO,
formato do webhook) muda os dois lados **no mesmo commit** — é a razão principal de isto ser um
monorepo. Detalhes em [`CLAUDE.md`](CLAUDE.md).
