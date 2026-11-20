package agent

import (
	"log/slog"

	"reasonix/internal/state/sessionstore"
)

// SavePromptUsageState writes what the gauge learned from provider usage beside
// the transcript at path. A caller that owns the save point passes the path it
// is writing; the agent's own session swap passes its bound one.
func (a *Agent) SavePromptUsageState(path string) {
	if a == nil {
		return
	}
	a.window().savePromptUsageState(path)
}

// savePromptUsageState writes the last reported prompt size tied to the view
// that carried it, plus the tokenizer ratio learned from the same request. Without
// them a resume estimates from the cold rate, which overcounts Chinese text: its
// punctuation falls outside isCJKRune and is priced at the Latin byte rate.
func (a *contextWindow) savePromptUsageState(path string) {
	if a == nil || path == "" {
		return
	}
	session := a.sess.session()
	if session == nil {
		return
	}
	calibration := a.sess.output.promptCalibration.Load()
	anchor := a.sess.output.usageAnchor.Load()
	if calibration == nil && (anchor == nil || anchor.promptTokens <= 0) {
		return
	}
	state := sessionstore.PromptUsageState{ModelRef: a.modelRef}
	if calibration != nil {
		state.Calibration = &sessionstore.PromptCalibration{
			PromptTokens: calibration.promptTokens,
			ViewChars:    calibration.requestChars,
			CompactChars: calibration.compactChars,
			CJKRunes:     calibration.cjkRunes,
			CJKBytes:     calibration.cjkBytes,
		}
	}
	if anchor != nil && anchor.promptTokens > 0 {
		state.Anchor = &sessionstore.PromptAnchor{
			PromptTokens:      anchor.promptTokens,
			ProjectionVersion: anchor.projectionVersion,
			ViewChars:         anchor.visible.requestChars,
			CompactChars:      anchor.visible.compactChars,
			CJKRunes:          anchor.visible.cjkRunes,
			CJKBytes:          anchor.visible.cjkBytes,
			ImageTokens:       anchor.visible.imageTokens,
		}
	}
	if err := session.SavePromptUsageState(path, state); err != nil {
		slog.Warn("agent: save prompt usage state", "err", err)
	}
}

// loadPromptUsageState restores that record on resume. The anchor's projection
// version still decides whether it describes the loaded view, so a fold made
// after the count was taken leaves the estimate in charge.
func (a *contextWindow) loadPromptUsageState(path string) {
	if a == nil {
		return
	}
	state, ok := sessionstore.LoadPromptUsageState(path)
	if !ok || state.ModelRef != a.modelRef {
		return
	}
	if calibration := state.Calibration; calibration != nil && calibration.PromptTokens > 0 && calibration.ViewChars > 0 {
		a.sess.output.promptCalibration.Store(&promptTokenCalibration{
			promptTokens: calibration.PromptTokens,
			requestChars: calibration.ViewChars,
			compactChars: calibration.CompactChars,
			cjkRunes:     calibration.CJKRunes,
			cjkBytes:     calibration.CJKBytes,
		})
	}
	if anchor := state.Anchor; anchor != nil && anchor.PromptTokens > 0 {
		a.sess.output.usageAnchor.Store(&usageAnchor{
			promptTokens: anchor.PromptTokens,
			visible: requestCalibrationShape{
				requestChars: anchor.ViewChars,
				compactChars: anchor.CompactChars,
				cjkRunes:     anchor.CJKRunes,
				cjkBytes:     anchor.CJKBytes,
				imageTokens:  anchor.ImageTokens,
			},
			projectionVersion: anchor.ProjectionVersion,
		})
	}
}
