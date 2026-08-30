package webhook

import (
	"crypto/hmac"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"io"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"
)

func init() {
	sleepFn = func(time.Duration) {} // noop: avoids real backoff in tests
}

func TestNotify_Success(t *testing.T) {
	var received Payload
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		json.NewDecoder(r.Body).Decode(&received) //nolint:errcheck
		w.WriteHeader(http.StatusOK)
	}))
	defer srv.Close()

	payload := Payload{VideoID: "video-123", Success: true}
	if err := Notify(srv.URL, "", payload); err != nil {
		t.Fatalf("Notify() should not return error: %v", err)
	}
	if received.VideoID != "video-123" {
		t.Fatalf("expected VideoID 'video-123', got '%s'", received.VideoID)
	}
	if !received.Success {
		t.Fatal("expected Success=true in received payload")
	}
}

func TestNotify_ContentTypeJSON(t *testing.T) {
	var contentType string
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		contentType = r.Header.Get("Content-Type")
		w.WriteHeader(http.StatusOK)
	}))
	defer srv.Close()

	Notify(srv.URL, "", Payload{VideoID: "v1", Success: true}) //nolint:errcheck

	if contentType != "application/json" {
		t.Fatalf("expected Content-Type 'application/json', got '%s'", contentType)
	}
}

func TestNotify_WithHMAC_CorrectSignature(t *testing.T) {
	secret := "my-secret"
	var signature string
	var bodyReceived []byte

	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		signature = r.Header.Get("X-Webhook-Signature")
		var err error
		bodyReceived, err = io.ReadAll(r.Body)
		if err != nil {
			t.Fatalf("failed to read request body: %v", err)
		}
		w.WriteHeader(http.StatusOK)
	}))
	defer srv.Close()

	payload := Payload{VideoID: "video-123", Success: true}
	if err := Notify(srv.URL, secret, payload); err != nil {
		t.Fatalf("Notify() should not return error: %v", err)
	}

	mac := hmac.New(sha256.New, []byte(secret))
	mac.Write(bodyReceived)
	expected := "sha256=" + hex.EncodeToString(mac.Sum(nil))

	if signature != expected {
		t.Fatalf("incorrect HMAC signature\nexpected:  %s\nreceived: %s", expected, signature)
	}
}

func TestNotify_NoSecret_NoHeader(t *testing.T) {
	var signature string
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		signature = r.Header.Get("X-Webhook-Signature")
		w.WriteHeader(http.StatusOK)
	}))
	defer srv.Close()

	Notify(srv.URL, "", Payload{VideoID: "v1", Success: true}) //nolint:errcheck

	if signature != "" {
		t.Fatalf("should not send X-Webhook-Signature without secret, got: '%s'", signature)
	}
}

func TestNotify_RetryOnFailure(t *testing.T) {
	attempts := 0
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		attempts++
		if attempts < 3 {
			w.WriteHeader(http.StatusInternalServerError)
			return
		}
		w.WriteHeader(http.StatusOK)
	}))
	defer srv.Close()

	if err := Notify(srv.URL, "", Payload{VideoID: "v1", Success: true}); err != nil {
		t.Fatalf("Notify() should succeed on the 3rd attempt, got: %v", err)
	}
	if attempts != 3 {
		t.Fatalf("expected 3 attempts, got %d", attempts)
	}
}

func TestNotify_ErrorAfter3Attempts(t *testing.T) {
	attempts := 0
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		attempts++
		w.WriteHeader(http.StatusBadGateway)
	}))
	defer srv.Close()

	err := Notify(srv.URL, "", Payload{VideoID: "v1", Success: true})
	if err == nil {
		t.Fatal("Notify() should return error after 3 failed attempts")
	}
	if attempts != 3 {
		t.Fatalf("expected 3 attempts, got %d", attempts)
	}
	if !strings.Contains(err.Error(), "3 attempts") {
		t.Fatalf("error message should mention '3 attempts', got: %s", err.Error())
	}
}

func TestNotify_InvalidURL(t *testing.T) {
	err := Notify("://invalid-url", "", Payload{VideoID: "v1", Success: true})
	if err == nil {
		t.Fatal("Notify() should return error with invalid URL")
	}
}

func TestNotify_ServerUnavailable(t *testing.T) {
	// Port with no listener
	err := Notify("http://localhost:19999", "", Payload{VideoID: "v1", Success: true})
	if err == nil {
		t.Fatal("Notify() should return error when server is unavailable")
	}
}

func TestPayload_JSONSerialization(t *testing.T) {
	p := Payload{
		VideoID:       "abc",
		Success:       true,
		ProcessedPath: "processed/abc",
	}

	data, err := json.Marshal(p)
	if err != nil {
		t.Fatalf("failed to serialize payload: %v", err)
	}

	if !strings.Contains(string(data), `"videoId":"abc"`) {
		t.Fatalf("'videoId' field should appear, got: %s", data)
	}
	if !strings.Contains(string(data), `"success":true`) {
		t.Fatalf("'success' field should appear, got: %s", data)
	}
}
