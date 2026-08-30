package processor_steps

import (
	"context"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestSegmentForStreaming_ValidVideo(t *testing.T) {
	inputPath := GenerateTestVideo(t, 5)
	outputDir := filepath.Join(t.TempDir(), "hls")

	if err := SegmentForStreaming(context.Background(), inputPath, outputDir); err != nil {
		t.Fatalf("SegmentForStreaming() failed: %v", err)
	}

	masterPath := filepath.Join(outputDir, "master.m3u8")
	if _, err := os.Stat(masterPath); os.IsNotExist(err) {
		t.Fatal("SegmentForStreaming() did not create master.m3u8")
	}

	entries, err := os.ReadDir(outputDir)
	if err != nil {
		t.Fatalf("error reading output directory: %v", err)
	}

	variantFound := false
	for _, e := range entries {
		if !e.IsDir() {
			continue
		}
		varDir := filepath.Join(outputDir, e.Name())
		playlistPath := filepath.Join(varDir, "playlist.m3u8")
		if _, err := os.Stat(playlistPath); os.IsNotExist(err) {
			t.Errorf("variant %s has no playlist.m3u8", e.Name())
			continue
		}
		segs, _ := os.ReadDir(varDir)
		tsCount := 0
		for _, s := range segs {
			if strings.HasSuffix(s.Name(), ".ts") {
				tsCount++
			}
		}
		if tsCount == 0 {
			t.Errorf("variant %s has no .ts segments", e.Name())
			continue
		}
		variantFound = true
	}
	if !variantFound {
		t.Error("SegmentForStreaming() did not generate any resolution variant")
	}
}

func TestSegmentForStreaming_InvalidVideo(t *testing.T) {
	invalidPath := CreateInvalidFile(t)
	outputDir := filepath.Join(t.TempDir(), "hls")

	if err := SegmentForStreaming(context.Background(), invalidPath, outputDir); err == nil {
		t.Error("SegmentForStreaming() should fail with invalid input")
	}
}

func TestSegmentForStreaming_NonExistentVideo(t *testing.T) {
	outputDir := filepath.Join(t.TempDir(), "hls")

	if err := SegmentForStreaming(context.Background(), "/path/that/does/not/exist.mp4", outputDir); err == nil {
		t.Error("SegmentForStreaming() should fail with non-existent file")
	}
}
