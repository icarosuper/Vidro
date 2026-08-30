# Getting Started — Video Processor

This worker does not expose an HTTP API to submit videos. The interface is the **Redis queue**: you publish a `videoID`, the worker processes it and delivers the artifacts to MinIO.

---

## Prerequisites

- Go 1.21+
- Docker and Docker Compose
- FFmpeg installed locally (to run outside Docker)

```bash
# Check if FFmpeg is available
ffmpeg -version
ffprobe -version
```

---

## 1. Configuration

```bash
cp .env-example .env
```

The `.env-example` already comes with default values for local development. Edit if necessary.

---

## 2. Start the infrastructure

```bash
docker-compose up -d redis minio
```

This starts:
- **Redis** at `localhost:6379`
- **MinIO** at `localhost:9000` (API) and `localhost:9001` (web console)

---

## 3. Start the worker

```bash
go run main.go
```

The worker will:
- Connect to Redis and MinIO
- Create the `videos` bucket if it doesn't exist
- Wait for jobs in the `video_queue` queue
- Expose `/health` and `/metrics` at `http://localhost:8080`

---

## 4. Upload the video

Before publishing a job, the video needs to be in MinIO at path `raw/{videoID}`.

### Via web console (easiest)

1. Access `http://localhost:9001`
2. Login: `minioadmin` / `minioadmin`
3. Create the `videos` bucket (if it doesn't exist)
4. Upload the video at `raw/my-video`

### Via MinIO CLI (`mc`)

```bash
# Install mc (if not available)
# https://min.io/docs/minio/linux/reference/minio-mc.html

mc alias set local http://localhost:9000 minioadmin minioadmin
mc cp my-video.mp4 local/videos/raw/my-video
```

### Via curl

```bash
curl -X PUT "http://localhost:9000/videos/raw/my-video" \
  -u minioadmin:minioadmin \
  --upload-file my-video.mp4
```

---

## 5. Publish the job

```bash
redis-cli LPUSH video_queue "my-video"
```

The worker will detect the job immediately and start processing.

---

## 6. Monitor processing

### Worker logs

Logs appear in the terminal where you ran `go run main.go`:

```
Step 1/7: Validating video
Step 2/7: Analyzing content
Step 3/7: Transcoding video
...
Video processed successfully
```

### Job state in Redis

```bash
# View current state (pending / processing / done / failed)
redis-cli GET job:my-video
```

Example response when completed:
```json
{
  "status": "done",
  "artifacts": {
    "video": "processed/my-video_processed",
    "thumbnails": "thumbnails/my-video",
    "audio": "audio/my-video.mp3",
    "preview": "preview/my-video_preview.mp4",
    "hls": "hls/my-video"
  },
  "metadata": {
    "duration": 120.5,
    "width": 1920,
    "height": 1080,
    "video_codec": "h264",
    "audio_codec": "aac",
    "fps": 30,
    "bitrate": 5000000,
    "size": 75000000
  },
  "retry_count": 0,
  "created_at": 1748304000,
  "updated_at": 1748304120
}
```

### Artifacts generated in MinIO

Access `http://localhost:9001` and browse the `videos` bucket:

| Path | Content |
|---|---|
| `processed/my-video_processed` | Transcoded MP4 (H.264/AAC) |
| `thumbnails/my-video/` | 5 JPG thumbnails |
| `audio/my-video.mp3` | Extracted audio track |
| `preview/my-video_preview.mp4` | Preview of the first 30s |
| `hls/my-video/master.m3u8` | HLS master playlist |
| `hls/my-video/240p/` | HLS 240p segments |
| `hls/my-video/360p/` | HLS 360p segments |
| `hls/my-video/720p/` | HLS 720p segments (if original resolution allows) |

### Success queue

When the job completes successfully, the processed video ID is published to `video_success_queue`:

```bash
redis-cli BRPOP video_success_queue 10
# Returns: "my-video_processed"
```

### Prometheus metrics

```bash
curl http://localhost:8080/metrics
```

### Health check

```bash
curl http://localhost:8080/health
# Returns: OK
```

---

## 7. Run everything via Docker Compose

To run the worker in a container too (without needing local Go):

```bash
docker-compose up --build
```

> The worker inside Docker uses env vars defined in `docker-compose.yml`.

---

## Environment Variables

| Variable | Default | Description |
|---|---|---|
| `REDIS_HOST` | `localhost:6379` | Redis address |
| `PROCESSING_REQUEST_QUEUE` | `video_queue` | Job input queue |
| `PROCESSING_FINISHED_QUEUE` | `video_success_queue` | Completed jobs queue |
| `MINIO_ENDPOINT` | `localhost:9000` | MinIO address |
| `MINIO_ROOT_USER` | `minioadmin` | MinIO user |
| `MINIO_ROOT_PASSWORD` | `minioadmin` | MinIO password |
| `MINIO_BUCKET_NAME` | `videos` | Bucket name |
| `MINIO_USE_SSL` | `false` | Enable SSL on MinIO |
| `HTTP_PORT` | `8080` | HTTP server port |
| `WORKER_COUNT` | CPU cores | Number of parallel workers |
| `MAX_FILE_SIZE_MB` | `5120` (5GB) | Maximum file size |
| `WEBHOOK_SECRET` | — | HMAC secret for signing webhooks |
| `OTEL_ENDPOINT` | — | OTLP endpoint for tracing (e.g. `localhost:4318`) |
| `OTEL_SERVICE_NAME` | `video-processor` | Service name in traces |

---

## Simulating failures and retries

To test the retry mechanism, publish a job with an ID that doesn't exist in MinIO:

```bash
redis-cli LPUSH video_queue "non-existent-video"
```

The worker will try 3 times and then move the job to the Dead Letter Queue:

```bash
# View jobs in DLQ
redis-cli LRANGE video_queue:dead 0 -1

# View error state
redis-cli GET job:non-existent-video
```
