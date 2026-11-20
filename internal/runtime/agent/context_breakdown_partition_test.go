package agent

import (
	"context"
	"encoding/json"
	"strings"
	"testing"

	"reasonix/internal/contract/provider"
	"reasonix/internal/contract/tool"
	"reasonix/internal/state/sessionstore"
)

// schemaHeavyRegistry is the ordinary shape of a coding session: a handful of
// tool schemas, each with prose and a JSON schema. The catalogue is a constant
// of the session, so any movement in the tool figure is the estimate's, not the
// prompt's.
func schemaHeavyRegistry() *tool.Registry {
	reg := tool.NewRegistry()
	for _, name := range []string{"read_file", "write_file", "bash", "grep", "glob"} {
		reg.Add(stubSchemaTool{name: name})
	}
	return reg
}

// stubSchemaTool is a tool with a real description and parameter schema, which
// is all the estimator reads.
type stubSchemaTool struct{ name string }

func (s stubSchemaTool) Name() string { return s.name }

func (s stubSchemaTool) Description() string {
	return "Read or write a file inside the workspace. The model uses this to inspect build scripts, " +
		"grep for symbols, and apply edits. Arguments are JSON."
}

func (s stubSchemaTool) Schema() json.RawMessage {
	return json.RawMessage(`{"type":"object","properties":{"path":{"type":"string","description":"file path to operate on"},"content":{"type":"string"},"pattern":{"type":"string"},"limit":{"type":"integer","minimum":1,"maximum":2000}},"required":["path"]}`)
}

func (s stubSchemaTool) Execute(context.Context, json.RawMessage) (string, error) { return "", nil }
func (s stubSchemaTool) ReadOnly() bool                                           { return true }

// The tool catalogue is the same bytes on every request, so its share of the
// prompt must not move as the conversation grows.
func TestToolCatalogueDoesNotMoveAsTheConversationGrows(t *testing.T) {
	sess := sessionstore.NewSession("sys")
	reg := schemaHeavyRegistry()
	a := New(nil, reg, sess, Options{ContextWindow: 200_000}, nil)
	before := a.ContextBreakdown().Tools
	if before <= 0 {
		t.Fatalf("the catalogue must be measured: %d", before)
	}

	// A real round, then provider telemetry: the ratio the estimate applies is
	// learned from a whole request, so a turn whose text mix differs from the
	// catalogue's used to re-price the catalogue with it.
	a.prepareSamplingRequest(context.Background())
	a.storeLatestRequestUsage(&provider.Usage{PromptTokens: before + 2_000})
	if got := a.ContextBreakdown().Tools; got != before {
		t.Fatalf("tool figure moved after calibration: %d -> %d", before, got)
	}

	for range 4 {
		sess.Add(provider.Message{Role: provider.RoleUser, Content: strings.Repeat("请检查构建脚本的内容与测试", 800)})
		sess.Add(provider.Message{Role: provider.RoleAssistant, Content: strings.Repeat("脚本会构建所有包并运行测试", 800)})
	}
	if got := a.ContextBreakdown().Tools; got != before {
		t.Fatalf("tool figure moved with the conversation: %d -> %d", before, got)
	}
}

// The system prompt is the same bytes on every request too.
func TestSystemPromptDoesNotMoveAsTheConversationGrows(t *testing.T) {
	sess := sessionstore.NewSession(strings.Repeat("You are a coding agent. ", 200))
	a := New(nil, tool.NewRegistry(), sess, Options{ContextWindow: 200_000}, nil)
	before := a.ContextBreakdown().System
	if before <= 0 {
		t.Fatalf("the system prompt must be measured: %d", before)
	}
	a.prepareSamplingRequest(context.Background())
	a.storeLatestRequestUsage(&provider.Usage{PromptTokens: before + 3_000})
	if got := a.ContextBreakdown().System; got != before {
		t.Fatalf("system figure moved after calibration: %d -> %d", before, got)
	}
	for range 6 {
		sess.Add(provider.Message{Role: provider.RoleUser, Content: strings.Repeat("请检查构建脚本的内容", 500)})
	}
	if got := a.ContextBreakdown().System; got != before {
		t.Fatalf("system figure moved from %d to %d as the conversation grew", before, got)
	}
}

// A compaction digest is what the whole folded history became. Told only by its
// role it was counted as a model reply — a class named "what the model said"
// that a long session then reads as the model's own prose.
func TestFoldDigestIsNotCountedAsAModelReply(t *testing.T) {
	sess := sessionstore.NewSession("sys")
	a := New(nil, nil, sess, Options{ContextWindow: 200_000}, nil)
	sess.Add(provider.Message{Role: provider.RoleUser, Content: "short question"})
	sess.Add(provider.Message{Role: provider.RoleAssistant, Content: "short answer"})
	before := a.ContextBreakdown()

	sess.Add(provider.Message{Role: provider.RoleUser, Content: SummaryTagOpen + strings.Repeat("a folded conversation, condensed. ", 500) + SummaryTagClose})
	after := a.ContextBreakdown()
	if after.Reply-before.Reply > 200 {
		t.Fatalf("reply grew by %d tokens: the fold digest was counted as the model's own words", after.Reply-before.Reply)
	}
	// The digest is a real part of the prompt, so it is measured — in a class of
	// its own. It is neither the instructions the host sends every round nor the
	// person's own words, and it grows with the conversation.
	if after.Digest <= 0 {
		t.Fatalf("the digest was not measured at all: before=%+v after=%+v", before, after)
	}
	if after.System != before.System || after.User != before.User {
		t.Fatalf("the digest leaked into a fixed class: before=%+v after=%+v", before, after)
	}
	if after.Prompt != before.Prompt+after.Digest {
		t.Fatalf("the classes no longer add up: before=%d after=%d digest=%d", before.Prompt, after.Prompt, after.Digest)
	}
}

// The gauge may prefer the provider's own count while the classes are priced
// piece by piece, so the two are different sums. The breakdown has to publish
// both and the gap between them rather than leave a panel to invent a
// relationship between a bar and a legend that do not add up.
func TestBreakdownNamesBothAccountsAndTheirGap(t *testing.T) {
	a, _ := newAnchorAgent(t)
	sendWithUsage(t, a, &provider.Usage{PromptTokens: 60_000})

	b := a.ContextBreakdown()
	sum := b.System + b.Tools + b.ProjectMemory + b.User + b.Reply + b.Output
	if sum == 0 {
		t.Fatalf("no class reported tokens: %+v", b)
	}
	if sum != b.Prompt {
		t.Fatalf("classes sum to %d but the breakdown reports %d", sum, b.Prompt)
	}
	if b.Budget <= 0 || b.Budget != b.Total {
		t.Fatalf("the panel has no denominator of its own: total=%d budget=%d", b.Total, b.Budget)
	}
	if b.Overhead != max(0, b.Total-b.Prompt) {
		t.Fatalf("overhead %d does not describe the gap between %d and %d", b.Overhead, b.Total, b.Prompt)
	}
	if b.Total > b.Prompt && b.Overhead == 0 {
		t.Fatal("a total above the classes reported no overhead")
	}
}
