package minio

import "testing"

func TestContentTypeByExt(t *testing.T) {
	cases := []struct {
		ext      string
		expected string
	}{
		{".jpg", "image/jpeg"},
		{".jpeg", "image/jpeg"},
		{".JPG", "image/jpeg"},  // case-insensitive
		{".mp3", "audio/mpeg"},
		{".mp4", "video/mp4"},
		{".ts", "video/MP2T"},
		{".m3u8", "application/x-mpegURL"},
		{".bin", "application/octet-stream"},
		{"", "application/octet-stream"},
	}

	for _, c := range cases {
		got := contentTypeByExt(c.ext)
		if got != c.expected {
			t.Errorf("contentTypeByExt(%q) = %q, expected %q", c.ext, got, c.expected)
		}
	}
}
