package main

import (
	"fmt"
	"regexp"
	"strings"

	"reasonix/internal/base/wiregen"
)

var (
	// csRecordOpenRe opens a generated record, so the block that follows can be
	// read as its property list.
	csRecordOpenRe = regexp.MustCompile(`(?m)^public sealed record (\w+)\s*$`)

	// csJsonNameRe reads the name a generated property carries on the wire. The
	// C# property name is not the contract — the JSON name is.
	csJsonNameRe = regexp.MustCompile(`\[JsonPropertyName\("([^"]+)"\)\]`)
)

// csRecordFields reads one generated record's wire names and the line it opens
// on. Reading the JsonPropertyName attribute rather than the C# property keeps
// the comparison over the payload rather than over a rendering choice.
func csRecordFields(body, typeName string) ([]string, int, bool) {
	for _, m := range csRecordOpenRe.FindAllStringSubmatchIndex(body, -1) {
		if body[m[2]:m[3]] != typeName {
			continue
		}
		block, _, ok := strings.Cut(body[m[1]:], "\n}")
		if !ok {
			return nil, 0, false
		}
		var out []string
		for _, attr := range csJsonNameRe.FindAllStringSubmatch(block, -1) {
			out = append(out, attr[1])
		}
		return out, strings.Count(body[:m[0]], "\n") + 1, true
	}
	return nil, 0, false
}

// csRecordName is the C# record a Go type renders to.
func csRecordName(m wireMirror) string {
	if m.goType == "Node" && strings.Contains(m.goFile, "/surfacespec/") {
		return "SurfaceNode"
	}
	return m.goType
}

// wiregenParityFindings compares the generated C# records against the Go types
// the mirror table pairs them with.
//
// The shell's records are generated, so a field cannot silently differ the way a
// hand-kept copy can — but the generator itself can be wrong, and a comparison
// that reads its output through an independent parser is the only thing that
// notices. A pair whose record was never rendered is skipped rather than failed:
// the mirror table covers serve and control views outside the event contract,
// and those keep their TypeScript mirror until the shell replaces it.
func wiregenParityFindings(root string, mirrors []wireMirror, goFields map[string][]string) []Finding {
	files, err := wiregen.Render(root)
	if err != nil {
		return []Finding{{wiregen.OutputDir, 1, ruleWireParity, err.Error(), 1}}
	}
	return wiregenParityFindingsFrom(files, mirrors, goFields)
}

// wiregenParityFindingsFrom is the comparison over an already-rendered set, so a
// test can carry one pair and the record it is about rather than the tree.
func wiregenParityFindingsFrom(files map[string]string, mirrors []wireMirror, goFields map[string][]string) []Finding {
	var out []Finding
	for _, m := range mirrors {
		name := csRecordName(m)
		body, rendered := files[name+".g.cs"]
		if !rendered {
			continue
		}

		fields, declared := goFields[m.goFile+"."+m.goType]
		if !declared {
			out = append(out, Finding{m.goFile, 1, ruleWireParity,
				fmt.Sprintf("%s is declared a mirrored wire type and is not a struct here", m.goType), 1})
			continue
		}

		csFields, line, found := csRecordFields(body, name)
		if !found {
			out = append(out, Finding{
				File:   wiregen.OutputDir + "/" + name + ".g.cs",
				Line:   1,
				Rule:   ruleWireParity,
				Msg:    fmt.Sprintf("%s mirrors %s.%s and is not declared here", name, m.goFile, m.goType),
				Weight: 1,
			})
			continue
		}

		at := wiregen.OutputDir + "/" + name + ".g.cs"
		for _, field := range missingFrom(fields, csFields) {
			out = append(out, Finding{at, line, ruleWireParity,
				fmt.Sprintf("%s sends %q and %s cannot read it", m.goType, field, name), 1})
		}
		for _, field := range missingFrom(csFields, fields) {
			out = append(out, Finding{at, line, ruleWireParity,
				fmt.Sprintf("%s reads %q and %s never sends it", name, field, m.goType), 1})
		}
	}
	return out
}
