# Test Data

This directory contains test files for unit tests.

## Generating a test video

To generate a valid test video, use FFmpeg:

```bash
# Generate test video (5 seconds, 640x480, with audio)
ffmpeg -f lavfi -i testsrc=duration=5:size=640x480:rate=30 \
       -f lavfi -i sine=frequency=1000:duration=5 \
       -pix_fmt yuv420p \
       test_video.mp4
```

Tests will create test videos automatically when needed.
