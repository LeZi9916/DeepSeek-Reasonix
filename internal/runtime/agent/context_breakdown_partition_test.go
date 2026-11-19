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
