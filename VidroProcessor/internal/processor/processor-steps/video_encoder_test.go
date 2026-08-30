package processor_steps

import "testing"

func TestNormalizeNVENCPreset(t *testing.T) {
	if got := NormalizeNVENCPreset(""); got != "p5" {
		t.Fatalf("empty: got %q want p5", got)
	}
	if got := NormalizeNVENCPreset("p7"); got != "p7" {
		t.Fatalf("p7: got %q", got)
	}
	if got := NormalizeNVENCPreset("P3"); got != "p3" {
		t.Fatalf("P3: got %q want p3", got)
	}
}
