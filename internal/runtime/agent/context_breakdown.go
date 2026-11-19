package agent

import (
	"math"
	"strings"

	"reasonix/internal/contract/event"
	"reasonix/internal/contract/provider"
)

// projectInstructionsTag is the transient block the host prepends to a turn for
// the workspace's standing instructions. It is spelled here because the
// breakdown must recognise the block on its own; control owns the renderer.
const projectInstructionsTag = "project-instructions"

// ContextBreakdown says where the prompt's tokens went. "How full" is only
// actionable once you know what is filling it — a memory file, a tool
// catalogue and a run of huge outputs are fixed in completely different ways.
// Each figure uses the estimator the compaction thresholds use, so the parts
// describe the same prompt the gauge does.
type ContextBreakdown struct {
	System int `json:"system"` // system prompt: base instructions, memory, skills
	Tools  int `json:"tools"`  // tool schemas the provider is sent
	// ProjectMemory is the standing-instruction block the host prepends to the
	// turn (<project-instructions>), measured apart from User because a reader
	// who sees it only as part of their own message cannot tell what to edit.
	ProjectMemory int `json:"projectMemory"`
	// ProjectMemoryFiles is that block's own breakdown, one entry per loaded
	// document in precedence order. Nil when nothing is loaded.
	ProjectMemoryFiles []ContextMemoryFile `json:"projectMemoryFiles,omitempty"`
	User               int                 `json:"user"`   // what the user typed
	Reply              int                 `json:"reply"`  // what the model said
	Output             int                 `json:"output"` // what tools returned
	Total              int                 `json:"total"`
	Window             int                 `json:"window"`
	// CompactAt is the lower of the two configured bounds, which is where this
	// session actually folds: a gauge drawn against the window instead can look
	// nearly empty at the moment maintenance happens.
	CompactAt int `json:"compactAt"`
	// Which bound that is, decided here rather than re-derived from the pair:
	// a reader told only the number cannot say why a large-window session folds,
	// and one comparing the two itself owns a second copy of the rule.
	Boundary string `json:"boundary"`
	// The window share, which is the bound the user configured and the one
	// they will look for when the other fires instead.
	CapacityAt int `json:"capacityAt"`
	// Largest is each message class's biggest single holders, largest first: the
	// totals above say a class is heavy, this says which message made it so.
	Largest []ContextHolder `json:"largest,omitempty"`
}

// ContextMemoryFile is one standing-instruction document the host loaded into
// the turn: where it came from and what it costs. Path is the reader's own
// spelling — the user's file or a workspace-relative one — never a rendering
// detail of the block, and Body is absent: the panel opens files where they
// live, not in a JSON response.
type ContextMemoryFile struct {
	Path   string `json:"path"`
	Scope  string `json:"scope"`
	Tokens int    `json:"tokens"`
}

// ContextHolder is one message among a class's largest. Class is the
// breakdown's own name for it (user, reply, output); Tool is the producing
// tool's name for a tool result; Active says it belongs to the turn still
// running, which a fold cannot take.
type ContextHolder struct {
	Class  string `json:"class"`
	Tool   string `json:"tool,omitempty"`
	Tokens int    `json:"tokens"`
	Active bool   `json:"active,omitempty"`
}

const largestHoldersPerClass = 5

// ContextBreakdown measures the visible request one message class at a time.
// A fold digest is what the folded history became, not something the model
// said, so it is measured with the system side rather than as a reply.
func (a *Agent) ContextBreakdown() ContextBreakdown {
	if a == nil {
		return ContextBreakdown{}
	}
	visible := a.window().modelVisibleMessages()
	// Measured first: estimating a slice of visible clears the creation stamps
	// that mark the running turn.
	largest := a.window().largestMessages(visible)
	memory, typed := splitProjectMemory(visible)
	system, replies := splitFoldDigests(visible)

	out := ContextBreakdown{
		Total:      a.ContextUsedTokens(),
		Window:     a.ContextWindow(),
		CompactAt:  a.window().compactTrigger(),
		Boundary:   a.window().compactBoundary(),
		CapacityAt: a.window().capacityCompactTrigger(),
		Tools:      a.window().classTokens(nil, true),
	}
	for _, class := range []struct {
		msgs []provider.Message
		to   *int
	}{
		{system, &out.System},
		{typed, &out.User},
		{replies, &out.Reply},
		{messagesWithRole(visible, provider.RoleTool), &out.Output},
	} {
		*class.to = a.window().classTokens(class.msgs, false)
	}
	out.ProjectMemory = a.window().classTokens(memory, false)
	out.Largest = holdersByClass(largest)
	return out
}

// splitFoldDigests routes a compaction digest to the system side. The split is
// on the message the fold installed, never on who has the role: a digest rides
// a user turn, and left there it read as what the person typed.
func splitFoldDigests(visible []provider.Message) (system, replies []provider.Message) {
	for _, m := range visible {
		switch {
		case m.Role == provider.RoleSystem || isCompactionSummary(m):
			system = append(system, m)
		case m.Role == provider.RoleAssistant:
			replies = append(replies, m)
		}
	}
	return system, replies
}

// splitProjectMemory separates the workspace's standing instructions from the
// text a person typed, so the block has a class of its own. The two results
// partition the same messages, so the classes still add up to the prompt the
// gauge counts; a tag inside someone's own prose stays theirs, because only the
// leading chain the host writes is walked.
func splitProjectMemory(visible []provider.Message) (memory, typed []provider.Message) {
	for _, m := range visible {
		if m.Role != provider.RoleUser {
			continue
		}
		// A fold digest rides a user turn but is not what the person typed; it
		// is measured with the system side.
		if isCompactionSummary(m) {
			continue
		}
		// Local-only records carry no host block, but they are still part of
		// what the user class measures.
		if !carriesStandingState(m) {
			typed = append(typed, m)
			continue
		}
		block, rest := splitLeadingBlock(m, projectInstructionsTag)
		if block.Content != "" {
			memory = append(memory, block)
		}
		if rest.Content != "" {
			typed = append(typed, rest)
		}
	}
	return memory, typed
}

// splitLeadingBlock divides one message into the leading transient blocks of
// tag and everything else. A message carrying neither is returned as rest.
func splitLeadingBlock(m provider.Message, tag string) (block, rest provider.Message) {
	content := m.Content
	var got, kept strings.Builder
	for {
		gotTag, _, end, ok := leadingTransientBlock(content)
		if !ok {
			break
		}
		if gotTag == tag {
			got.WriteString(content[:end])
		} else {
			kept.WriteString(content[:end])
		}
		content = content[end:]
	}
	kept.WriteString(content)
	block, rest = m, m
	// The two halves carry the same tool calls, and a call's arguments are
	// measured: leaving them on both counts the same bytes twice. They belong to
	// the half the person's own message became.
	block.ToolCalls = nil
	block.Content = got.String()
	rest.Content = kept.String()
	return block, rest
}

// holdersByClass keeps the largest few of each class, in the order the
// messages arrive: largest first within a class.
func holdersByClass(items []event.ContextItem) []ContextHolder {
	var out []ContextHolder
	kept := map[string]int{}
	for _, it := range items {
		class := messageClass(provider.Role(it.Role))
		if class == "" || kept[class] >= largestHoldersPerClass {
			continue
		}
		kept[class]++
		out = append(out, ContextHolder{Class: class, Tool: it.Tool, Tokens: it.Tokens, Active: it.Active})
	}
	return out
}

func messageClass(role provider.Role) string {
	switch role {
	case provider.RoleUser:
		return "user"
	case provider.RoleAssistant:
		return "reply"
	case provider.RoleTool:
		return "output"
	}
	return ""
}

// classTokens estimates one slice of the request at the cold rate, from its own
// bytes. The session's calibration is learned from a whole request, so applying
// it here moved every class whenever the conversation's text mix moved.
func (a *contextWindow) classTokens(msgs []provider.Message, schemas bool) int {
	req := provider.Request{MaxTokens: a.maxOutputTokens, Temperature: provider.OptionalTemperature(a.temperature)}
	if len(msgs) > 0 {
		projected := a.providerProjectionMessages(provider.ModelMessages(msgs))
		for i := range projected {
			projected[i].CreatedAt = 0
		}
		req.Messages = projected
	}
	if schemas {
		req.Tools = a.estimationSurface()
	}
	if len(req.Messages) == 0 && len(req.Tools) == 0 {
		return 0
	}
	return fixedTokens(a.requestCalibrationShape(req))
}

// fixedTokens prices a shape at the cold rate from its own bytes.
func fixedTokens(shape requestCalibrationShape) int {
	if shape.requestChars <= 0 && shape.imageTokens <= 0 {
		return 0
	}
	return int(math.Ceil(float64(shape.requestChars)*fallbackTokPerChar)) + int(shape.imageTokens)
}

func messagesWithRole(msgs []provider.Message, role provider.Role) []provider.Message {
	out := make([]provider.Message, 0, len(msgs))
	for _, m := range msgs {
		if m.Role == role {
			out = append(out, m)
		}
	}
	return out
}

// estimationSurface is the tool surface an estimate is measured against: what
// the last request actually carried, or the whole provider-visible set before
// one has gone out — which overstates rather than understates.
func (a *contextWindow) estimationSurface() []provider.ToolSchema {
	if a == nil {
		return nil
	}
	if sent := a.sess.lastProviderSchemas; len(sent) > 0 {
		return sent
	}
	if a.svc.tools == nil {
		return nil
	}
	return a.svc.tools.Schemas()
}
