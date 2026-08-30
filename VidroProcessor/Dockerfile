# Dockerfile for the video processing worker
FROM golang:1.25-alpine AS builder

WORKDIR /app
COPY . .

# Download dependencies
RUN go mod download

# Build the binary
RUN go build -o video-processor main.go

# Final image
FROM alpine:latest
WORKDIR /app
COPY --from=builder /app/video-processor .

# Install ffmpeg
RUN apk add --no-cache ffmpeg

# Default command
CMD ["./video-processor"]
