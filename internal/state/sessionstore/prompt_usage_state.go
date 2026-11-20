package sessionstore

import (
	"encoding/json"
	"os"
	"time"

	"reasonix/internal/base/fileutil"
	"reasonix/internal/state/store"
)

// PromptUsageState is the gauge half a process learns from provider usage: the
// last reported prompt size tied to the view that carried it, and the tokenizer
// ratio learned from the same request. Neither survives a restart on its own, so
// a resumed session without this record estimates from the cold rate, which
// overcounts Chinese text.
type PromptUsageState struct {
	SchemaVersion int `json:"schema_version"`
	// ModelRef is the model that reported these figures. A count and a ratio
	// belong to the tokenizer that produced them, so another model's record is
	// not a starting point for this session.
	ModelRef    string             `json:"model_ref,omitempty"`
	Calibration *PromptCalibration `json:"calibration,omitempty"`
	Anchor      *PromptAnchor      `json:"anchor,omitempty"`
	UpdatedAt   time.Time          `json:"updated_at"`
}

// PromptCalibration is tokens learned per provider-visible character.
type PromptCalibration struct {
	PromptTokens int   `json:"prompt_tokens"`
	ViewChars    int64 `json:"view_chars"`
	CompactChars int64 `json:"compact_chars,omitempty"`
	CJKRunes     int64 `json:"cjk_runes,omitempty"`
	CJKBytes     int64 `json:"cjk_bytes,omitempty"`
}

// PromptAnchor is one provider count tied to the visible view that carried it
// and the generation it was measured under. A count from another generation
// describes a context the fold has already replaced.
type PromptAnchor struct {
	PromptTokens      int    `json:"prompt_tokens"`
	ProjectionVersion uint64 `json:"projection_version,omitempty"`
	ViewChars         int64  `json:"view_chars"`
	CompactChars      int64  `json:"compact_chars,omitempty"`
	CJKRunes          int64  `json:"cjk_runes,omitempty"`
	CJKBytes          int64  `json:"cjk_bytes,omitempty"`
	ImageTokens       int64  `json:"image_tokens,omitempty"`
}

const PromptUsageStateSchemaVersion = 1

// promptUsagePath is where the record lives, given the session's own path.
func promptUsagePath(sessionPath string) string {
	return store.SessionPromptUsage(sessionPath)
}

// SavePromptUsageState publishes the record atomically. A session with no path
// writes nothing: a headless run has no restart to open on.
func SavePromptUsageState(sessionPath string, st PromptUsageState) error {
	path := promptUsagePath(sessionPath)
	if path == "" {
		return nil
	}
	st.SchemaVersion = PromptUsageStateSchemaVersion
	st.UpdatedAt = time.Now().UTC()
	b, err := json.Marshal(st)
	if err != nil {
		return err
	}
	return fileutil.AtomicWriteFile(path, append(b, '\n'), 0o600)
}

// SavePromptUsageState publishes the record under this session's write
// authority, exactly as a fold does: a controller whose transcript save would
// be refused must not write beside it either.
func (s *Session) SavePromptUsageState(sessionPath string, st PromptUsageState) error {
	releaseAuth, err := s.requireWriteAuthorityForSave(sessionPath)
	if err != nil {
		return err
	}
	defer releaseAuth()
	return SavePromptUsageState(sessionPath, st)
}

// LoadPromptUsageState reports ok=false when there is nothing usable to read. A
// missing file is a session that never sent a request; a corrupt or newer one
// is not a reason to refuse the conversation, only to open on the estimate.
func LoadPromptUsageState(sessionPath string) (PromptUsageState, bool) {
	path := promptUsagePath(sessionPath)
	if path == "" {
		return PromptUsageState{}, false
	}
	b, err := os.ReadFile(path)
	if err != nil {
		return PromptUsageState{}, false
	}
	var st PromptUsageState
	if err := json.Unmarshal(b, &st); err != nil || st.SchemaVersion != PromptUsageStateSchemaVersion {
		return PromptUsageState{}, false
	}
	return st, true
}
