package serve

import (
	"encoding/json"
	"errors"
	"net/http"
	"net/http/httptest"
	"testing"

	"reasonix/internal/contract/config"
	"reasonix/internal/session/control"
)

func conflictCodeOf(t *testing.T, resp *http.Response) string {
	t.Helper()
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusConflict {
		t.Fatalf("status = %d, want 409", resp.StatusCode)
	}
	var got struct {
		Code string `json:"code"`
	}
	if err := json.NewDecoder(resp.Body).Decode(&got); err != nil {
		t.Fatal(err)
	}
	return got.Code
}

// The write is on disk by the time the rebuild is refused, so a runtime that
// still has work is a save that applies later, not a malfunction: every route
// that saves then rebuilds must answer with that identity.
func TestSavesWhileBusyReportSavedNotFailed(t *testing.T) {
	for _, tc := range []struct{ name, path, body string }{
		{"shell", "/shell", `{"prefer":"","path":""}`},
		{"roles", "/roles", `{"role":"planner","ref":""}`},
	} {
		t.Run(tc.name, func(t *testing.T) {
			srv := newRichProviderServerAs(t, func(c control.SessionAPI) control.SessionAPI { return midTurn{c} })
			if got := conflictCodeOf(t, postProvider(t, srv.URL, tc.path, tc.body)); got != "runtime.saved_while_running" {
				t.Fatalf("code = %q, want runtime.saved_while_running", got)
			}
		})
	}
}

func TestRebuildFailedKeepsGenuineFailuresDistinct(t *testing.T) {
	rec := httptest.NewRecorder()
	rebuildFailed(rec, errors.New("boom"))
	if got := conflictCodeOf(t, rec.Result()); got != "runtime.rebuild_failed" {
		t.Fatalf("code = %q, want runtime.rebuild_failed", got)
	}
}

func compactRatio(t *testing.T, base, body string) *http.Response {
	t.Helper()
	return postProvider(t, base, "/compaction/ratio", body)
}

// refusalStatusCode reads a refusal's dotted identity at the status the route
// promised, so a test asserts on what the frontend branches on rather than on
// the sentence beside it.
func refusalStatusCode(t *testing.T, resp *http.Response, status int) string {
	t.Helper()
	defer resp.Body.Close()
	if resp.StatusCode != status {
		b, _ := readAllString(resp)
		t.Fatalf("status = %d, want %d: %s", resp.StatusCode, status, b)
	}
	var got struct {
		Code string `json:"code"`
	}
	if err := json.NewDecoder(resp.Body).Decode(&got); err != nil {
		t.Fatal(err)
	}
	return got.Code
}

// The threshold is a share of the window, so the panel has to be able to move
// it and see where the session now folds — the rebuilt trigger is the proof
// that the write reached the agent and not only the file.
func TestSaveCompactRatioMovesTheTrigger(t *testing.T) {
	srv := newRichProviderServer(t)

	resp := compactRatio(t, srv.URL, `{"ratio":0.6}`)
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		b, _ := readAllString(resp)
		t.Fatalf("POST /compaction/ratio = %d: %s", resp.StatusCode, b)
	}
	var got control.CompactionSettings
	if err := json.NewDecoder(resp.Body).Decode(&got); err != nil {
		t.Fatal(err)
	}
	if got.Ratio != 0.6 {
		t.Fatalf("ratio = %v, want 0.6", got.Ratio)
	}
	// The trigger is derived from whatever window the rebuilt agent runs under,
	// so assert the relation rather than a number that belongs to the fixture.
	if got.ContextWindow <= 0 {
		t.Fatalf("the rebuild reported no context window: %+v", got)
	}
	if want := int(float64(got.ContextWindow) * 0.6); got.Trigger != want {
		t.Fatalf("trigger = %d, want %d after the rebuild", got.Trigger, want)
	}

	cfg, err := config.Load()
	if err != nil {
		t.Fatal(err)
	}
	if cfg.Agent.CompactRatio != 0.6 {
		t.Fatalf("saved ratio = %v, want 0.6", cfg.Agent.CompactRatio)
	}
}

// A share outside the open window fraction is refused before anything is
// written, so the answer is a bad request and the stored value is untouched.
func TestSaveCompactRatioRefusesAFractionThatFoldsNowhere(t *testing.T) {
	srv := newRichProviderServer(t)
	before := config.LoadForEdit(config.UserConfigPath()).Agent.CompactRatio

	for _, body := range []string{`{"ratio":1.2}`, `{"ratio":0}`, `{"ratio":-0.2}`} {
		if code := refusalStatusCode(t, compactRatio(t, srv.URL, body), http.StatusBadRequest); code != "compaction.ratio_out_of_range" {
			t.Fatalf("%s: code = %q, want compaction.ratio_out_of_range", body, code)
		}
	}
	if after := config.LoadForEdit(config.UserConfigPath()).Agent.CompactRatio; after != before {
		t.Fatalf("a refused fraction changed the stored ratio: %v -> %v", before, after)
	}
}

func TestSaveCompactRatioRefusesAMissingField(t *testing.T) {
	srv := newRichProviderServer(t)
	if code := refusalStatusCode(t, compactRatio(t, srv.URL, `{}`), http.StatusBadRequest); code != codeMissingField {
		t.Fatalf("code = %q, want %q", code, codeMissingField)
	}
}

// The ratio is bound at assembly, so a running turn keeps the one it started
// under. The write landed; saying so is the difference between "applies after
// this turn" and a malfunction.
func TestSaveCompactRatioMidTurnSaysItAppliesAfterTheTurn(t *testing.T) {
	srv := newRichProviderServerAs(t, func(c control.SessionAPI) control.SessionAPI { return midTurn{c} })

	if code := refusalStatusCode(t, compactRatio(t, srv.URL, `{"ratio":0.55}`), http.StatusConflict); code != "compaction.saved_while_running" {
		t.Fatalf("code = %q, want compaction.saved_while_running", code)
	}
	if got := config.LoadForEdit(config.UserConfigPath()).Agent.CompactRatio; got != 0.55 {
		t.Fatalf("ratio on disk = %v, want the saved 0.55", got)
	}
}
