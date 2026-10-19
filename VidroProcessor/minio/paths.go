package minio

// Object-key layout inside the bucket. Shared contract with VidroApi, locked by
// contracts/minio-paths.json — both sides test the same file.

func RawObject(videoID string) string {
	return getObjectPath(VideoTypeRaw, videoID)
}

func RawArchivedObject(videoID string) string {
	return getObjectPath(VideoTypeRawArchived, videoID)
}

func ProcessedObjectID(videoID string) string {
	return videoID + "_processed"
}

func ProcessedObject(videoID string) string {
	return getObjectPath(VideoTypeProcessed, ProcessedObjectID(videoID))
}

func AudioObject(videoID string) string {
	return "audio/" + videoID + ".mp3"
}

func PreviewObject(videoID string) string {
	return "preview/" + videoID + "_preview.mp4"
}

func ThumbnailsPrefix(videoID string) string {
	return "thumbnails/" + videoID
}

func HLSPrefix(videoID string) string {
	return "hls/" + videoID
}
