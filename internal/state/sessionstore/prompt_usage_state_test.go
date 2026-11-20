package sessionstore

import (
	"os"
	"path/filepath"
	"testing"

	"reasonix/internal/base/testenv"
)

func TestPromptUsageStateRoundTrips(t *testing.T) {
	path := filepath.Join(testenv.TempDir(t), "session.jsonl")
	want := PromptUsageState{
		ModelRef: "p/m",
		Calibration: &PromptCalibration{
			PromptTokens: 8_300, ViewChars: 40_000, CompactChars: 39_000, CJKRunes: 12_000, CJKBytes: 36_000,
		},
		Anchor: &PromptAnchor{
			PromptTokens: 8_343, ProjectionVersion: 7, ViewChars: 40_100, CompactChars: 39_100,
			CJKRunes: 12_100, CJKBytes: 36_300, ImageTokens: 5,
		},
	}
	if err := SavePromptUsageState(path, want); err != nil {
		t.Fatalf("SavePromptUsageState: %v", err)
	}
	got, ok := LoadPromptUsageState(path)
	if !ok {
		t.Fatal("the record just written did not load")
	}
	if got.SchemaVersion != PromptUsageStateSchemaVersion || got.ModelRef != want.ModelRef || got.UpdatedAt.IsZero() {
		t.Fatalf("record header = %+v", got)
	}
	if got.Calibration == nil || *got.Calibration != *want.Calibration {
		t.Fatalf("calibration = %+v, want %+v", got.Calibration, want.Calibration)
	}
	if got.Anchor == nil || *got.Anchor != *want.Anchor {
		t.Fatalf("anchor = %+v, want %+v", got.Anchor, want.Anchor)
	}
}

func TestPromptUsageStateAbsentOrUnreadableIsNoRecord(t *testing.T) {
	dir := testenv.TempDir(t)
	if _, ok := LoadPromptUsageState(filepath.Join(dir, "missing.jsonl")); ok {
		t.Fatal("a session with no record reported one")
	}
	path := filepath.Join(dir, "corrupt.jsonl")
	if err := os.WriteFile(promptUsagePath(path), []byte("{not json"), 0o600); err != nil {
		t.Fatal(err)
	}
	if _, ok := LoadPromptUsageState(path); ok {
		t.Fatal("a corrupt record was read as usable")
	}
}

func TestPromptUsageStateRefusesANewerSchema(t *testing.T) {
	path := filepath.Join(testenv.TempDir(t), "session.jsonl")
	newer := []byte(`{"schema_version":99,"anchor":{"prompt_tokens":1}}`)
	if err := os.WriteFile(promptUsagePath(path), newer, 0o600); err != nil {
		t.Fatal(err)
	}
	if _, ok := LoadPromptUsageState(path); ok {
		t.Fatal("a schema this build does not know was read as usable")
	}
}

func TestPromptUsageStateWritesNoRecordWithoutAPath(t *testing.T) {
	if err := SavePromptUsageState("", PromptUsageState{}); err != nil {
		t.Fatalf("a headless session must write nothing, got %v", err)
	}
	if _, ok := LoadPromptUsageState(""); ok {
		t.Fatal("a session with no path reported a record")
	}
}
