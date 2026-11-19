package agent

import (
	"reasonix/internal/state/sessionstore"
	"strings"
	"testing"

	"reasonix/internal/contract/provider"
)

// The gauge says a session is at 70%; this says what put it there. Each class
// is measured with the same estimator the thresholds use, so the parts describe
// the same prompt the gauge does rather than a second opinion about it.
func TestContextBreakdownSeparatesWhatFillsTheWindow(t *testing.T) {
	sess := sessionstore.NewSession("你是一个助手")
	a := New(nil, nil, sess, Options{ContextWindow: 128000}, nil)
	sess.Add(provider.Message{Role: provider.RoleUser, Content: "把这个仓库跑一遍测试"})
	sess.Add(provider.Message{Role: provider.RoleAssistant, Content: "好，我先看构建脚本"})
	sess.Add(provider.Message{Role: provider.RoleTool, Content: string(make([]byte, 4000))})

	b := a.ContextBreakdown()
	if b.User == 0 || b.Reply == 0 || b.Output == 0 {
		t.Fatalf("every class that has messages must report tokens: %+v", b)
	}
	if b.Output <= b.User {
		t.Fatalf("a 4KB tool result must outweigh a one-line prompt: %+v", b)
	}
	if b.Total == 0 {
		t.Fatalf("total must match the gauge, got %+v", b)
	}
}

// Only the user class is split. A version that regrouped every message would
// report the system prompt and the tool results as what the person typed.
func TestContextBreakdownKeepsOtherClassesOutOfTheUserFigure(t *testing.T) {
	sess := sessionstore.NewSession("你是一个助手")
	a := New(nil, nil, sess, Options{ContextWindow: 128000}, nil)
	sess.Add(provider.Message{Role: provider.RoleUser, Content: "把这个仓库跑一遍测试"})
	sess.Add(provider.Message{Role: provider.RoleTool, Content: string(make([]byte, 4000))})

	b := a.ContextBreakdown()
	if b.Output <= b.User {
		t.Fatalf("user = %d, output = %d: the tool result was counted as what the person typed", b.User, b.Output)
	}
	if b.User > 100 {
		t.Fatalf("user = %d for a one-line prompt: another class leaked into it", b.User)
	}
}

// A session with no turns reports nothing for the classes a turn fills, and the
// system prompt stays in its own class.
func TestContextBreakdownReportsNoUserFigureBeforeAnyTurn(t *testing.T) {
	a := New(nil, nil, sessionstore.NewSession("你是一个助手"), Options{ContextWindow: 128000}, nil)
	b := a.ContextBreakdown()
	if b.User != 0 || b.Reply != 0 || b.Output != 0 {
		t.Fatalf("a fresh session has no turns yet: %+v", b)
	}
	if b.System == 0 {
		t.Fatalf("the system prompt belongs to its own class, not the user's: %+v", b)
	}
}

// The breakdown carries the boundary in force, read from the same rule that
// fires it rather than recomputed by whoever draws it.
func TestContextBreakdownCarriesTheBoundaryInForce(t *testing.T) {
	a := New(nil, nil, sessionstore.NewSession("你是一个助手"), Options{ContextWindow: 1_000_000}, nil)
	if got, want := a.ContextBreakdown().CompactAt, 900_000; got != want {
		t.Fatalf("CompactAt = %d, want capacity boundary %d against a 1M window", got, want)
	}

	// Under a window small enough that its capacity share is the lower of the
	// two, the same field must follow the other bound.
	small := New(nil, nil, sessionstore.NewSession("你是一个助手"), Options{ContextWindow: 128_000}, nil)
	if got, want := small.ContextBreakdown().CompactAt, small.window().compactTrigger(); got != want {
		t.Fatalf("CompactAt = %d, want the capacity share %d", got, want)
	}
	if small.ContextBreakdown().CompactAt >= small.ContextBreakdown().Window {
		t.Fatal("a capacity-bound session must fold before its window, not at it")
	}
}

// An undeclared window is what turns automatic maintenance off, and it must
// read as "no boundary" rather than as a boundary of zero tokens away.
func TestContextBreakdownReportsNoBoundaryWithoutAWindow(t *testing.T) {
	a := New(nil, nil, sessionstore.NewSession("你是一个助手"), Options{}, nil)
	if got := a.ContextBreakdown().CompactAt; got != 0 {
		t.Fatalf("CompactAt = %d, want 0 when nobody declared a window", got)
	}
}

// A class total says tool output is the largest part; it cannot say which
// result it was. The breakdown names the largest holders of each class, with
// the producing tool, so the reader can act on the one that matters.
func TestContextBreakdownNamesTheLargestHoldersOfEachClass(t *testing.T) {
	a := compositionAgent(t)
	b := a.ContextBreakdown()
	if len(b.Largest) == 0 {
		t.Fatalf("no holders named: %+v", b)
	}
	var tools []ContextHolder
	for _, h := range b.Largest {
		if h.Class == "output" {
			tools = append(tools, h)
		}
		if h.Class != "user" && h.Class != "reply" && h.Class != "output" {
			t.Fatalf("holder class %q is not one of the breakdown's own message classes", h.Class)
		}
		if h.Tokens <= 0 {
			t.Fatalf("a holder with no tokens: %+v", h)
		}
	}
	if len(tools) == 0 || tools[0].Tool != "grep" || !tools[0].Active {
		t.Fatalf("output holders = %+v, want the running turn's grep result first", tools)
	}
	for i := 1; i < len(tools); i++ {
		if tools[i].Tokens > tools[i-1].Tokens {
			t.Fatalf("holders of a class must run largest first: %+v", tools)
		}
	}
}

func TestContextBreakdownKeepsAClassToItsFiveLargestHolders(t *testing.T) {
	sess := sessionstore.NewSession("sys")
	a := New(nil, nil, sess, Options{ContextWindow: 128000}, nil)
	for i := range 9 {
		sess.Add(provider.Message{Role: provider.RoleUser, Content: string(make([]byte, 100*(i+1)))})
	}
	n := 0
	for _, h := range a.ContextBreakdown().Largest {
		if h.Class == "user" {
			n++
		}
	}
	if n != 5 {
		t.Fatalf("user holders = %d, want the five largest of nine", n)
	}
}

// The workspace's rules arrive inside the user turn, so a role split counts them
// as what the person typed. A reader looking at that figure has nothing to edit;
// the class of its own is what makes the file it came from actionable.
func TestContextBreakdownMovesProjectMemoryOutOfTheUserClass(t *testing.T) {
	sess := sessionstore.NewSession("sys")
	a := New(nil, nil, sess, Options{ContextWindow: 128000}, nil)
	typed := strings.Repeat("u", 2000)
	rules := strings.Repeat("r", 8000)
	sess.Add(provider.Message{Role: provider.RoleUser, Content: "<project-instructions>\n" + rules + "\n</project-instructions>\n\n" + typed})

	b := a.ContextBreakdown()
	if b.ProjectMemory <= 0 {
		t.Fatalf("project memory reported %d tokens for an 8KB block: %+v", b.ProjectMemory, b)
	}
	if b.ProjectMemory <= b.User {
		t.Fatalf("the block is four times the typed text but reports smaller: memory=%d user=%d", b.ProjectMemory, b.User)
	}
	// The block is still the same turn: what is measured must not vanish, or the
	// classes stop adding up to the prompt the gauge counts.
	if b.ProjectMemory+b.User <= b.ProjectMemory {
		t.Fatalf("the user class contributed nothing after the split: %+v", b)
	}
}

// A tag the person typed is their own prose. Splitting on it would move their
// words into a class that names a file they never wrote.
func TestContextBreakdownLeavesATagInTypedTextAlone(t *testing.T) {
	sess := sessionstore.NewSession("sys")
	a := New(nil, nil, sess, Options{ContextWindow: 128000}, nil)
	sess.Add(provider.Message{Role: provider.RoleUser, Content: "why is <project-instructions> shaped like that?"})

	if b := a.ContextBreakdown(); b.ProjectMemory != 0 {
		t.Fatalf("typed prose was counted as project memory: %+v", b)
	}
}

// A session with no rules has no class to draw, and a zero figure must not be
// mistaken for a block that arrived empty.
func TestContextBreakdownReportsNoProjectMemoryWithoutRules(t *testing.T) {
	a := New(nil, nil, sessionstore.NewSession("sys"), Options{ContextWindow: 128000}, nil)
	b := a.ContextBreakdown()
	if b.ProjectMemory != 0 || len(b.ProjectMemoryFiles) != 0 {
		t.Fatalf("a session with no instructions reported memory: %+v", b)
	}
}
