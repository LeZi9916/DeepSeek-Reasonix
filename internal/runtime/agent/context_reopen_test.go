package agent

import (
	"path/filepath"
	"strings"
	"testing"

	"reasonix/internal/base/testenv"
	"reasonix/internal/contract/event"
	"reasonix/internal/contract/provider"
	"reasonix/internal/contract/tool"
	"reasonix/internal/state/sessionstore"
)

// longFoldedSession is a conversation whose bulk is behind an installed fold:
// the digest covers almost everything, and only the last turns are verbatim.
func longFoldedSession() []provider.Message {
	msgs := []provider.Message{{Role: provider.RoleSystem, Content: "you are a coding agent"}}
	for range 30 {
		msgs = append(msgs,
			provider.Message{Role: provider.RoleUser, Content: strings.Repeat("请检查构建脚本并运行测试，", 60)},
			provider.Message{Role: provider.RoleAssistant, Content: strings.Repeat("脚本已构建完成，", 60)},
		)
	}
	msgs = append(msgs,
		provider.Message{Role: provider.RoleUser, Content: "最后的问题"},
		provider.Message{Role: provider.RoleAssistant, Content: "最后的回答"},
	)
	return msgs
}

// A session reopened after a restart has to read the same prompt it read before
// it. The fold in its sidecar is what keeps the visible view small; when a read
// lands before that sidecar is adopted, the view widens to the whole transcript
// and the gauge reports the pre-fold history — the "90% after restart" that
// falls once a later turn reads the fold.
func TestAReopenedSessionReadsThroughItsFoldFromTheFirstRead(t *testing.T) {
	path := filepath.Join(testenv.TempDir(t), "session.jsonl")
	msgs := longFoldedSession()

	// The writer folds and persists the projection beside the transcript.
	folding := New(nil, tool.NewRegistry(), &sessionstore.Session{Messages: msgs}, Options{
		ContextWindow: 100_000, CompactRatio: 0.9, WorkspaceID: "ws", ModelRef: "p/m",
		ArchiveDir: testenv.TempDir(t),
	}, event.Discard)
	folding.BindSessionPath(path, false)
	if _, err := folding.CompactNow(t.Context(), CompactRequest{IgnoreEconomics: true}); err != nil {
		t.Fatalf("fold: %v", err)
	}
	beforeFold := folding.ContextUsedTokens()
	if beforeFold <= 0 {
		t.Fatalf("the folded session reported %d tokens", beforeFold)
	}

	// The restart: a new agent, the same transcript, the sidecar adopted.
	reopened := New(nil, tool.NewRegistry(), &sessionstore.Session{Messages: msgs}, Options{
		ContextWindow: 100_000, CompactRatio: 0.9, WorkspaceID: "ws", ModelRef: "p/m",
	}, event.Discard)
	reopened.BindSessionPath(path, true)

	first := reopened.ContextUsedTokens()
	if first != beforeFold {
		t.Fatalf("the first read after a restart reported %d against %d before it: the fold was not in the view yet",
			first, beforeFold)
	}

	// The breakdown must describe that same small prompt, not the whole log.
	b := reopened.ContextBreakdown()
	if b.Total != first {
		t.Fatalf("breakdown total %d, gauge %d", b.Total, first)
	}
	if b.Prompt > first*2 {
		t.Fatalf("classes sum to %d against a %d-token prompt: the breakdown read past the fold", b.Prompt, first)
	}
}
