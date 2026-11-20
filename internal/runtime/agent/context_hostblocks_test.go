package agent

import (
	"strings"
	"testing"

	"reasonix/internal/contract/provider"
	"reasonix/internal/state/sessionstore"
)

// A user turn carries the host's runtime blocks ahead of what the person typed:
// the date, the skill catalogue, the execution policy, the scratch directory.
// Only the workspace rules had a class of their own, so every other block was
// measured as the person's own words — a session with a short question and a
// long catalogue reported the catalogue as theirs.
func TestContextBreakdownKeepsHostBlocksOutOfTheUserClass(t *testing.T) {
	sess := sessionstore.NewSession("sys")
	a := New(nil, nil, sess, Options{ContextWindow: 200_000}, nil)

	typed := "把构建跑一遍"
	hostBlocks := "<available-skills>\n" + strings.Repeat("s", 6000) + "\n</available-skills>\n" +
		"<execution-policy>\n" + strings.Repeat("p", 4000) + "\n</execution-policy>\n" +
		"<current-date>\n2026-11-20\n</current-date>\n"
	sess.Add(provider.Message{Role: provider.RoleUser, Content: hostBlocks + typed})

	b := a.ContextBreakdown()
	if b.User > 100 {
		t.Fatalf("user = %d for a nine-character question: %d bytes of host blocks were counted as theirs",
			b.User, len(hostBlocks))
	}
}

// Nothing may vanish: whatever the split calls those blocks, they are still part
// of the prompt, so the classes have to keep adding up to what the gauge reads.
func TestContextBreakdownAccountsForEveryHostBlock(t *testing.T) {
	sess := sessionstore.NewSession("sys")
	a := New(nil, nil, sess, Options{ContextWindow: 200_000}, nil)

	hostBlocks := "<available-skills>\n" + strings.Repeat("s", 6000) + "\n</available-skills>\n" +
		"<execution-policy>\n" + strings.Repeat("p", 4000) + "\n</execution-policy>\n"
	sess.Add(provider.Message{Role: provider.RoleUser, Content: hostBlocks + "把构建跑一遍"})
	sess.Add(provider.Message{Role: provider.RoleAssistant, Content: strings.Repeat("好的。", 100)})

	withHost := a.ContextBreakdown()

	// The same conversation without the host blocks, to isolate what they cost.
	plain := New(nil, nil, sessionstore.NewSession("sys"), Options{ContextWindow: 200_000}, nil)
	plain.Session().Add(provider.Message{Role: provider.RoleUser, Content: "把构建跑一遍"})
	plain.Session().Add(provider.Message{Role: provider.RoleAssistant, Content: strings.Repeat("好的。", 100)})
	withoutHost := plain.ContextBreakdown()

	// 10KB of blocks is roughly 2500 tokens at the cold rate; the user figure
	// must not have absorbed them.
	if grew := withHost.User - withoutHost.User; grew > 200 {
		t.Fatalf("the user class grew by %d tokens when the host blocks were added", grew)
	}
	// And what they cost has to appear somewhere.
	if totalWith, totalWithout := withHost.Prompt, withoutHost.Prompt; totalWith-totalWithout < 2000 {
		t.Fatalf("the classes grew by only %d tokens for 10KB of host blocks: %+v",
			totalWith-totalWithout, withHost)
	}
}

// The classes are what the gauge's figure is made of, so they have to add up to
// it. They were priced one way and the total another — the total by the Latin
// bytes-per-token rate, the classes by script — so a Chinese session reported a
// legend larger than the usage above it by a fifth.
func TestContextClassesAddUpToTheUsageFigure(t *testing.T) {
	for name, body := range map[string]string{
		"chinese": strings.Repeat("检查构建脚本并运行测试，", 120),
		"latin":   strings.Repeat("check the build and run the tests ", 120),
	} {
		sess := sessionstore.NewSession(strings.Repeat("system prompt. ", 40))
		a := New(nil, nil, sess, Options{ContextWindow: 200_000}, nil)
		for range 8 {
			sess.Add(provider.Message{Role: provider.RoleUser, Content: body})
			sess.Add(provider.Message{Role: provider.RoleAssistant, Content: body})
		}

		b := a.ContextBreakdown()
		if b.Prompt != b.Total {
			t.Fatalf("%s: classes sum to %d against a usage figure of %d", name, b.Prompt, b.Total)
		}
	}
}
