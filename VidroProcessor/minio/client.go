package minio

import (
	"context"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"video-processor/config"
	"video-processor/internal/circuitbreaker"

	"github.com/minio/minio-go/v7"
	"github.com/minio/minio-go/v7/pkg/credentials"
	"github.com/minio/minio-go/v7/pkg/lifecycle"
	"github.com/rs/zerolog/log"
)

type VideoType string

const (
	VideoTypeRaw        VideoType = "raw"
	VideoTypeProcessed  VideoType = "processed"
	VideoTypeRawArchived VideoType = "raw-archived"
)

// rawArchivedLifecycleDays is the number of days archived raws are retained before being deleted.
const rawArchivedLifecycleDays = 30

var (
	client *minio.Client
	cfg    *config.Config
)

const token = "" // TODO: Ver se precisa adicionar esse token

func InitMinioClient(config *config.Config) {
	cfg = config

	var err error
	client, err = minio.New(cfg.MinioEndpoint, &minio.Options{
		Creds:  credentials.NewStaticV4(cfg.MinioRootUser, cfg.MinioRootPassword, token),
		Secure: cfg.MinioUseSSL,
	})

	if err != nil {
		log.Fatal().Err(err).Msg("Failed to initialize MinIO client")
	}

	exists, err := client.BucketExists(context.Background(), cfg.MinioBucketName)
	if err != nil {
		log.Fatal().Err(err).Str("bucket", cfg.MinioBucketName).Msg("Failed to check if bucket exists")
	}

	log.Info().Str("bucket", cfg.MinioBucketName).Bool("exists", exists).Msg("MinIO bucket status")

	if !exists {
		err = client.MakeBucket(context.Background(), cfg.MinioBucketName, minio.MakeBucketOptions{})
		if err != nil {
			log.Fatal().Err(err).Str("bucket", cfg.MinioBucketName).Msg("Failed to create bucket")
		}
		log.Info().Str("bucket", cfg.MinioBucketName).Msg("Bucket created successfully")
	}

	configureRawArchivedLifecycle()
}

// configureRawArchivedLifecycle configures the lifecycle rule that automatically deletes
// objects in raw-archived/ after rawArchivedLifecycleDays days.
func configureRawArchivedLifecycle() {
	ctx := context.Background()
	prefix := string(VideoTypeRawArchived) + "/"
	lcConfig := lifecycle.NewConfiguration()
	lcConfig.Rules = []lifecycle.Rule{
		{
			ID:     "expire-raw-archived",
			Status: "Enabled",
			RuleFilter: lifecycle.Filter{
				Prefix: prefix,
			},
			Expiration: lifecycle.Expiration{
				Days: lifecycle.ExpirationDays(rawArchivedLifecycleDays),
			},
		},
	}
	if err := client.SetBucketLifecycle(ctx, cfg.MinioBucketName, lcConfig); err != nil {
		log.Warn().Err(err).Msg("Failed to configure lifecycle rule for raw-archived")
	} else {
		log.Info().Int("days", rawArchivedLifecycleDays).Str("prefix", prefix).Msg("Lifecycle rule configured for raw-archived")
	}
}

func getObjectPath(videoType VideoType, objectID string) string {
	return string(videoType) + "/" + objectID
}

func DownloadVideo(videoType VideoType, objectID, destPath string) error {
	_, err := circuitbreaker.MinIO.Execute(func() (interface{}, error) {
		return nil, downloadVideo(videoType, objectID, destPath)
	})
	return err
}

func downloadVideo(videoType VideoType, objectID, destPath string) error {
	ctx := context.Background()
	objectPath := getObjectPath(videoType, objectID)

	info, err := client.StatObject(ctx, cfg.MinioBucketName, objectPath, minio.StatObjectOptions{})
	if err != nil {
		log.Error().Err(err).Str("object", objectPath).Msg("Object not found")
		return err
	}
	if maxBytes := cfg.MaxFileSizeMB * 1024 * 1024; info.Size > maxBytes {
		return fmt.Errorf("video too large: %.0fMB (maximum: %dMB)", float64(info.Size)/1024/1024, cfg.MaxFileSizeMB)
	}

	object, err := client.GetObject(ctx, cfg.MinioBucketName, objectPath, minio.GetObjectOptions{})
	if err != nil {
		log.Error().Err(err).Str("object", objectPath).Msg("Failed to get object")
		return err
	}
	defer object.Close()
	log.Info().Str("object", objectPath).Msg("Download started")

	outFile, err := os.Create(destPath)
	if err != nil {
		log.Error().Err(err).Str("destPath", destPath).Msg("Failed to create destination file")
		return err
	}
	defer outFile.Close()

	if _, err := outFile.ReadFrom(object); err != nil {
		log.Error().Err(err).Str("object", objectPath).Msg("Failed to read object")
		return err
	}
	log.Info().Str("object", objectPath).Str("destPath", destPath).Msg("Download completed")

	return nil
}

func UploadVideo(srcPath string, videoType VideoType, objectID string) error {
	_, err := circuitbreaker.MinIO.Execute(func() (interface{}, error) {
		return nil, uploadVideo(srcPath, videoType, objectID)
	})
	return err
}

func uploadVideo(srcPath string, videoType VideoType, objectID string) error {
	ctx := context.Background()
	file, err := os.Open(srcPath)
	if err != nil {
		return fmt.Errorf("[minio] failed to open file for upload: %w", err)
	}
	defer file.Close()

	fileInfo, err := file.Stat()
	if err != nil {
		return fmt.Errorf("[minio] failed to get file info: %w", err)
	}

	objectPath := getObjectPath(videoType, objectID)
	_, err = client.PutObject(ctx, cfg.MinioBucketName, objectPath, file, fileInfo.Size(), minio.PutObjectOptions{ContentType: "video/mp4"})
	if err != nil {
		return fmt.Errorf("failed to upload: %w", err)
	}
	log.Info().Str("object", objectPath).Str("bucket", cfg.MinioBucketName).Int64("size", fileInfo.Size()).Msg("Upload completed")
	return nil
}

// UploadFile uploads any file to a specific path in MinIO.
func UploadFile(srcPath, objectPath string) error {
	_, err := circuitbreaker.MinIO.Execute(func() (interface{}, error) {
		return nil, uploadFile(srcPath, objectPath)
	})
	return err
}

func uploadFile(srcPath, objectPath string) error {
	ctx := context.Background()
	file, err := os.Open(srcPath)
	if err != nil {
		return fmt.Errorf("[minio] failed to open file for upload: %w", err)
	}
	defer file.Close()

	fileInfo, err := file.Stat()
	if err != nil {
		return fmt.Errorf("[minio] failed to get file info: %w", err)
	}

	_, err = client.PutObject(ctx, cfg.MinioBucketName, objectPath, file, fileInfo.Size(),
		minio.PutObjectOptions{ContentType: contentTypeByExt(filepath.Ext(srcPath))})
	if err != nil {
		return fmt.Errorf("failed to upload %s: %w", objectPath, err)
	}
	log.Info().Str("object", objectPath).Int64("size", fileInfo.Size()).Msg("Upload completed")
	return nil
}

// UploadDirectory recursively uploads all files from srcDir to MinIO
// with the given objectPrefix, preserving the subfolder structure.
func UploadDirectory(srcDir, objectPrefix string) error {
	entries, err := os.ReadDir(srcDir)
	if err != nil {
		return fmt.Errorf("[minio] failed to read directory %s: %w", srcDir, err)
	}
	for _, entry := range entries {
		srcPath := filepath.Join(srcDir, entry.Name())
		objectPath := objectPrefix + "/" + entry.Name()
		if entry.IsDir() {
			if err := UploadDirectory(srcPath, objectPath); err != nil {
				return err
			}
			continue
		}
		if err := UploadFile(srcPath, objectPath); err != nil {
			return err
		}
	}
	return nil
}

func contentTypeByExt(ext string) string {
	switch strings.ToLower(ext) {
	case ".jpg", ".jpeg":
		return "image/jpeg"
	case ".mp3":
		return "audio/mpeg"
	case ".mp4":
		return "video/mp4"
	case ".ts":
		return "video/MP2T"
	case ".m3u8":
		return "application/x-mpegURL"
	default:
		return "application/octet-stream"
	}
}

// ArchiveRawVideo moves the raw from raw/videoID to raw-archived/videoID and deletes the original.
// The object in raw-archived/ will be automatically deleted after rawArchivedLifecycleDays days.
func ArchiveRawVideo(videoID string) error {
	_, err := circuitbreaker.MinIO.Execute(func() (interface{}, error) {
		return nil, archiveRawVideo(videoID)
	})
	return err
}

func archiveRawVideo(videoID string) error {
	ctx := context.Background()
	srcPath := getObjectPath(VideoTypeRaw, videoID)
	dstPath := getObjectPath(VideoTypeRawArchived, videoID)

	// CopyObject in MinIO is the way to "move" — there is no native rename operation
	src := minio.CopySrcOptions{Bucket: cfg.MinioBucketName, Object: srcPath}
	dst := minio.CopyDestOptions{Bucket: cfg.MinioBucketName, Object: dstPath}
	if _, err := client.CopyObject(ctx, dst, src); err != nil {
		return fmt.Errorf("failed to copy raw to archive: %w", err)
	}

	if err := client.RemoveObject(ctx, cfg.MinioBucketName, srcPath, minio.RemoveObjectOptions{}); err != nil {
		return fmt.Errorf("failed to remove original raw after archiving: %w", err)
	}

	log.Info().Str("src", srcPath).Str("dst", dstPath).Int("expire_days", rawArchivedLifecycleDays).Msg("Raw archived successfully")
	return nil
}

// HealthCheck checks whether the MinIO client is healthy.
func HealthCheck() error {
	ctx := context.Background()
	_, err := client.BucketExists(ctx, cfg.MinioBucketName)
	return err
}
