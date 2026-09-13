package webhook

import (
	"bytes"
	"context"
	"crypto/hmac"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"net/http"
	"time"
)

// Payload is the body sent to the API callback when a job finishes.
// Field names use camelCase to match the VidroApi VideoProcessed contract
// (deserialized with PropertyNameCaseInsensitive = true).
type Payload struct {
	VideoID         string   `json:"videoId"`
	Success         bool     `json:"success"`
	ProcessedPath   string   `json:"processedPath,omitempty"`
	PreviewPath     string   `json:"previewPath,omitempty"`
	HlsPath         string   `json:"hlsPath,omitempty"`
	AudioPath       string   `json:"audioPath,omitempty"`
	ThumbnailPaths  []string `json:"thumbnailPaths,omitempty"`
	FileSizeBytes   *int64   `json:"fileSizeBytes,omitempty"`
	DurationSeconds *float64 `json:"durationSeconds,omitempty"`
	Width           *int     `json:"width,omitempty"`
	Height          *int     `json:"height,omitempty"`
	Codec           string   `json:"codec,omitempty"`
}

var httpClient = &http.Client{Timeout: 10 * time.Second}

var sleepFn = time.Sleep

// Notify sends the payload to callbackURL with up to 3 attempts and exponential backoff.
// If secret is non-empty, signs the body with HMAC-SHA256 in the X-Webhook-Signature header.
// If correlationID is non-empty, it goes out as X-Correlation-ID — the API reuses an inbound
// value, so the callback lands in its log under the same ID as the upload that started the job.
// Returns an error only if all attempts fail.
func Notify(callbackURL, secret, correlationID string, payload Payload) error {
	body, err := json.Marshal(payload)
	if err != nil {
		return fmt.Errorf("failed to serialize webhook payload: %w", err)
	}

	var lastErr error
	for attempt := 1; attempt <= 3; attempt++ {
		if err := send(callbackURL, secret, correlationID, body); err != nil {
			lastErr = err
			if attempt < 3 {
				sleepFn(time.Duration(attempt*attempt) * time.Second)
			}
			continue
		}
		return nil
	}
	return fmt.Errorf("webhook failed after 3 attempts: %w", lastErr)
}

func send(url, secret, correlationID string, body []byte) error {
	// context.Background() and not the job context on purpose: the webhook is sent from a
	// defer, after the job is over, and often while the job context is already canceled —
	// inheriting it would drop the notification the API is waiting for. The 10s
	// httpClient.Timeout is what bounds this call.
	req, err := http.NewRequestWithContext(context.Background(), http.MethodPost, url, bytes.NewReader(body))
	if err != nil {
		return fmt.Errorf("failed to create request: %w", err)
	}
	req.Header.Set("Content-Type", "application/json")
	if correlationID != "" {
		req.Header.Set("X-Correlation-ID", correlationID)
	}

	if secret != "" {
		mac := hmac.New(sha256.New, []byte(secret))
		mac.Write(body)
		req.Header.Set("X-Webhook-Signature", "sha256="+hex.EncodeToString(mac.Sum(nil)))
	}

	resp, err := httpClient.Do(req)
	if err != nil {
		return fmt.Errorf("failed to send webhook: %w", err)
	}
	defer resp.Body.Close()

	if resp.StatusCode < 200 || resp.StatusCode >= 300 {
		return fmt.Errorf("webhook returned unexpected status: %d", resp.StatusCode)
	}
	return nil
}
