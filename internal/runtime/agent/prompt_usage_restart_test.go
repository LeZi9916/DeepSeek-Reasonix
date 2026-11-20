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

// cjkSession is a Chinese conversation. One han rune is three bytes and less
// than one token, so the cold estimate prices this above what the provider
// counts — the shape that showed 90% after a restart and 80% after a reply.
func cjkSession() *sessionstore.Session {
	msgs := []provider.Message{{Role: provider.RoleSystem, Content: "system"}}
	for range 20 {
		msgs = append(msgs,
			provider.Message{Role: provider.RoleUser, Content: strings.Repeat("请检查构建脚本并运行测试，", 40)},
			provider.Message{Role: provider.RoleAssistant, Content: strings.Repeat("脚本已构建完成，", 40)},
		)
	}
	return &sessionstore.Session{Messages: msgs}
}

func newCJKSessionAgent(t *testing.T) *Agent {
	t.Helper()
	return New(nil, tool.NewRegistry(), cjkSession(), Options{
		ContextWindow: 100_000, CompactRatio: 0.9, WorkspaceID: "ws", ModelRef: "p/m",
		ArchiveDir: testenv.TempDir(t),
	}, event.Discard)
}

// A restart has to open on the figure the previous process closed on. The
// provider's count and the tokenizer ratio learned from it are process state;
// without them the resumed session read the cold estimate until a reply
// reported one, which is the 90%-then-80% a restart showed.
func TestRestartKeepsTheProviderReportedPromptSize(t *testing.T) {
	path := filepath.Join(testenv.TempDir(t), "session.jsonl")
	first := newCJKSessionAgent(t)
	first.BindSessionPath(path, false)
	cold := first.ContextUsedTokens()
	reported := cold * 8 / 10
	if cold <= reported*11/10 {
		t.Fatalf("setup: the cold estimate %d must sit well above the reported %d", cold, reported)
	}
	sendWithUsage(t, first, &provider.Usage{PromptTokens: reported})
	first.SavePromptUsageState(path)

	reopened := newCJKSessionAgent(t)
	reopened.BindSessionPath(path, true)
	if got := reopened.ContextUsedTokens(); got != reported {
		t.Fatalf("the first read after a restart reported %d, want the provider's %d it closed on (cold estimate %d)",
			got, reported, cold)
	}
	if reopened.window().sess.output.promptCalibration.Load() == nil {
		t.Fatal("the tokenizer ratio learned before the restart did not come back")
	}
}

// The ratio is what keeps a resumed session's estimate right once the anchor
// ages out. Without it every later estimate returns to the cold rate.
func TestRestartKeepsTheTokenizerRatio(t *testing.T) {
	path := filepath.Join(testenv.TempDir(t), "session.jsonl")
	first := newCJKSessionAgent(t)
	first.BindSessionPath(path, false)
	cold := first.ContextUsedTokens()
	reported := cold * 8 / 10
	sendWithUsage(t, first, &provider.Usage{PromptTokens: reported})
	first.SavePromptUsageState(path)

	reopened := newCJKSessionAgent(t)
	reopened.BindSessionPath(path, true)
	reopened.window().sess.output.usageAnchor.Store(nil)
	got := reopened.ContextUsedTokens()
	if got >= cold {
		t.Fatalf("estimate after the restart = %d, at or above the cold %d: the ratio was lost", got, cold)
	}
	if got < reported*9/10 || got > reported*11/10 {
		t.Fatalf("estimate after the restart = %d, want about the provider's %d", got, reported)
	}
}

// A count describes the context that carried it. A fold replaces that context,
// so a saved count from the previous generation must not stand in for the new
// one — the resumed session reads the estimate until a request re-anchors.
func TestRestartIgnoresACountFromBeforeTheFold(t *testing.T) {
	path := filepath.Join(testenv.TempDir(t), "session.jsonl")
	folding := New(&summaryCounter{}, tool.NewRegistry(), cjkSession(), Options{
		ContextWindow: 100_000, CompactRatio: 0.9, WorkspaceID: "ws", ModelRef: "p/m",
		ArchiveDir: testenv.TempDir(t),
	}, event.Discard)
	folding.BindSessionPath(path, false)
	reported := folding.ContextUsedTokens() / 2
	sendWithUsage(t, folding, &provider.Usage{PromptTokens: reported})
	if _, err := folding.CompactNow(t.Context(), CompactRequest{IgnoreEconomics: true}); err != nil {
		t.Fatalf("fold: %v", err)
	}
	folding.SavePromptUsageState(path)

	reopened := New(&summaryCounter{}, tool.NewRegistry(), cjkSession(), Options{
		ContextWindow: 100_000, CompactRatio: 0.9, WorkspaceID: "ws", ModelRef: "p/m",
	}, event.Discard)
	reopened.BindSessionPath(path, true)
	if got := reopened.ContextUsedTokens(); got == reported {
		t.Fatalf("the resumed gauge read the pre-fold %d; that count describes a context the fold replaced", got)
	}
}
