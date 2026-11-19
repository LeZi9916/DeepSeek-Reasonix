// compaction_settings.go — the compaction trigger as HTTP: what it is set to,
// what the session is actually running under, and the ratio the reader changes.
package serve

import (
	"errors"
	"net/http"

	"reasonix/internal/contract/config"
)

func (s *Server) registerCompactionRoutes(mux *http.ServeMux) {
	mux.HandleFunc("GET /compaction", s.compactionSettings)
	mux.HandleFunc("POST /compaction/ratio", s.saveCompactRatio)
}

func (s *Server) compactionSettings(w http.ResponseWriter, _ *http.Request) {
	writeJSON(w, s.ctl().CompactionSettings())
}

// saveCompactRatio writes the fold threshold as a fraction of the model window
// and rebuilds, because the ratio is bound into the agent at assembly. An
// out-of-range fraction is a bad request rather than a failed save: nothing
// was written.
func (s *Server) saveCompactRatio(w http.ResponseWriter, r *http.Request) {
	var body struct {
		Ratio *float64 `json:"ratio"`
	}
	if !decodeBody(w, r, &body) {
		return
	}
	if body.Ratio == nil {
		missingField(w, "ratio")
		return
	}
	if err := s.ctl().SaveCompactRatio(*body.Ratio); err != nil {
		if errors.Is(err, config.ErrCompactRatioRange) {
			refuse(w, http.StatusBadRequest, "compaction.ratio_out_of_range", err.Error(),
				map[string]any{"min": config.CompactRatioMin, "max": config.CompactRatioMax})
			return
		}
		saveFailed(w, http.StatusInternalServerError, "compaction.save_failed", err)
		return
	}
	// The write is on disk, so a running turn keeps the threshold it was built
	// with and the change counts from the next one: reporting that as a failed
	// save would send the reader looking for a malfunction.
	if err := s.rebuildInPlace(r.Context()); err != nil {
		if isSwitchBusy(err) {
			busy(w, "compaction.saved_while_running", "the threshold was saved; it applies once the running work finishes", nil)
			return
		}
		rebuildFailed(w, err)
		return
	}
	writeJSON(w, s.ctl().CompactionSettings())
}
