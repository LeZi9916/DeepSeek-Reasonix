// Package wiregen renders the kernel's wire contract as C# records.
//
// The Go structs are the contract, so the shell's records are generated rather
// than written twice: a hand-kept copy is where a rename lands on one side only.
// Rendering lives in a package rather than a command so repolint can hold the
// committed files to a fresh render — drift from the source reads the same as a
// stale hand-kept copy, and only reading both sides notices it.
package wiregen

import (
	"fmt"
	"go/ast"
	"go/parser"
	"go/token"
	"os"
	"path/filepath"
	"sort"
	"strconv"
	"strings"
)

// OutputDir is where the rendered records live, relative to the repository root.
// Every file carries the generated marker, so repolint excludes them from its
// size and comment budgets the way it excludes any other generated artifact.
const OutputDir = "desktop/avalonia/src/Reasonix.Studio.Contracts/Wire"

// primaryPackage is the package whose every exported struct is rendered: the
// event stream every frontend reads. The others are followed only where its
// fields point.
const primaryPackage = "internal/contract/eventwire"

// roots are the packages a field may be followed into.
var roots = []string{
	"internal/contract/eventwire",
	"internal/contract/pricing",
	"internal/contract/delivery",
	"internal/contract/agentgraph",
	"internal/contract/event",
	"internal/contract/provider",
	"internal/contract/surfacespec",
}

// packageAliases maps a Go import name to its repository-relative directory.
// Only a package listed here can be rendered from; one missing is reported
// rather than guessed at, because a silently skipped field is a field the shell
// would read as absent forever.
var packageAliases = map[string]string{
	"eventwire":   "internal/contract/eventwire",
	"pricing":     "internal/contract/pricing",
	"delivery":    "internal/contract/delivery",
	"agentgraph":  "internal/contract/agentgraph",
	"event":       "internal/contract/event",
	"provider":    "internal/contract/provider",
	"surfacespec": "internal/contract/surfacespec",
}

// clrNames maps a Go builtin to the C# type that reads it.
var clrNames = map[string]string{
	"string":  "string",
	"bool":    "bool",
	"int":     "int",
	"int32":   "int",
	"int64":   "long",
	"uint":    "uint",
	"uint32":  "uint",
	"uint64":  "ulong",
	"float32": "float",
	"float64": "double",
}

// valueTypes are the C# types that need a nullable marker when absent.
var valueTypes = map[string]bool{
	"int": true, "long": true, "uint": true, "ulong": true,
	"bool": true, "float": true, "double": true,
}

// decl is one exported Go type found in a package.
type decl struct {
	pkg  string
	name string
	doc  string
	spec *ast.TypeSpec
}

// renderer walks the declared packages once and renders what it finds.
type renderer struct {
	root    string
	fset    *token.FileSet
	decls   map[string]decl
	files   map[string]string
	emitted map[string]bool
}

// Render returns every C# file the contract renders to, keyed by file name.
//
// The result is deterministic: the same sources produce byte-identical files,
// which is what lets a caller treat a difference as drift.
func Render(root string) (map[string]string, error) {
	r := &renderer{
		root:    root,
		fset:    token.NewFileSet(),
		decls:   map[string]decl{},
		files:   map[string]string{},
		emitted: map[string]bool{},
	}

	// Pass one indexes every exported type, so a field can be followed wherever
	// it points without a second parse.
	for _, dir := range roots {
		if err := r.index(dir); err != nil {
			return nil, err
		}
	}

	// Pass two renders the primary package's own structs. A type it references
	// elsewhere is rendered when a field first needs it. Rendering a whole
	// referenced package would drag in types no payload carries — pricing's
	// embedded rate rows, for one — and demand a C# shape for something the
	// shell never receives.
	if err := r.renderPackage(primaryPackage); err != nil {
		return nil, err
	}

	// .editorconfig states CRLF for *.cs, and a generated file is a *.cs like any
	// other. Converting here keeps a comparison against what a write produces.
	for name, body := range r.files {
		r.files[name] = toCRLF(body)
	}
	return r.files, nil
}

// Check reports the first committed file that differs from a fresh render.
//
// This is the parity check for the C# side: the shell's records are generated,
// so what can go wrong is a source edit without a regeneration, and a stale file
// is exactly what that looks like.
func Check(root string) error {
	files, err := Render(root)
	if err != nil {
		return err
	}

	dir := filepath.Join(root, filepath.FromSlash(OutputDir))
	names := make([]string, 0, len(files))
	for name := range files {
		names = append(names, name)
	}
	sort.Strings(names)

	for _, name := range names {
		onDisk, err := os.ReadFile(filepath.Join(dir, name))
		if err != nil {
			return fmt.Errorf("%s/%s: %w; run go run ./tools/wiregen", OutputDir, name, err)
		}
		if string(onDisk) != files[name] {
			return fmt.Errorf("%s/%s is stale; run go run ./tools/wiregen", OutputDir, name)
		}
	}
	return nil
}

// index records every exported type the directory declares.
func (r *renderer) index(dir string) error {
	abs := filepath.Join(r.root, filepath.FromSlash(dir))
	entries, err := os.ReadDir(abs)
	if err != nil {
		return err
	}

	alias := ""
	for name, target := range packageAliases {
		if target == dir {
			alias = name
		}
	}

	for _, entry := range entries {
		name := entry.Name()
		if entry.IsDir() || !strings.HasSuffix(name, ".go") || strings.HasSuffix(name, "_test.go") {
			continue
		}
		file, err := parser.ParseFile(r.fset, filepath.Join(abs, name), nil, parser.ParseComments)
		if err != nil {
			return err
		}
		for _, d := range file.Decls {
			gen, ok := d.(*ast.GenDecl)
			if !ok || gen.Tok != token.TYPE {
				continue
			}
			for _, spec := range gen.Specs {
				ts, ok := spec.(*ast.TypeSpec)
				if !ok || !ts.Name.IsExported() {
					continue
				}
				doc := ""
				if gen.Doc != nil {
					doc = commentText(gen.Doc)
				} else if ts.Doc != nil {
					doc = commentText(ts.Doc)
				}
				r.decls[alias+"."+ts.Name.Name] = decl{
					pkg:  dir,
					name: ts.Name.Name,
					doc:  doc,
					spec: ts,
				}
			}
		}
	}
	return nil
}

// commentText joins a comment group into one sentence.
func commentText(group *ast.CommentGroup) string {
	var parts []string
	for _, c := range group.List {
		line := strings.TrimPrefix(c.Text, "//")
		line = strings.TrimPrefix(line, "/*")
		line = strings.TrimSuffix(line, "*/")
		line = strings.TrimSpace(line)
		if line != "" {
			parts = append(parts, line)
		}
	}
	return strings.Join(parts, " ")
}

// renderPackage renders every exported struct a package declares.
func (r *renderer) renderPackage(dir string) error {
	keys := make([]string, 0, len(r.decls))
	for key, d := range r.decls {
		if d.pkg == dir {
			keys = append(keys, key)
		}
	}
	sort.Strings(keys)

	for _, key := range keys {
		if _, err := r.renderType(key); err != nil {
			return err
		}
	}
	return nil
}

// renderType renders one declared type and returns its C# name. A type that is
// not a struct renders as a string wrapper: every such type in this contract is
// a JSON string with a closed set of values.
func (r *renderer) renderType(key string) (string, error) {
	d, ok := r.decls[key]
	if !ok {
		return "", fmt.Errorf("%s is referenced but not declared in a rendered package", key)
	}

	name := clrName(key)
	if r.emitted[name] {
		return name, nil
	}
	r.emitted[name] = true

	st, isStruct := d.spec.Type.(*ast.StructType)
	if !isStruct {
		r.files[name+".g.cs"] = emitAlias(name, d.doc)
		return name, nil
	}

	fields, err := r.renderFields(key, st)
	if err != nil {
		return "", err
	}

	r.files[name+".g.cs"] = emitRecord(name, d.doc, fields)
	return name, nil
}

// renderFields renders one struct's exported, serialised fields.
func (r *renderer) renderFields(key string, st *ast.StructType) ([]renderedField, error) {
	var out []renderedField
	for _, field := range st.Fields.List {
		if len(field.Names) == 0 {
			return nil, fmt.Errorf("%s: embedded fields are not rendered", key)
		}
		jsonName, keep, optional := wireName(field)
		if !keep {
			continue
		}
		for _, id := range field.Names {
			if !id.IsExported() {
				continue
			}
			clr, err := r.clrType(key, field.Type)
			if err != nil {
				return nil, fmt.Errorf("%s.%s: %w", key, id.Name, err)
			}
			doc := ""
			if field.Doc != nil {
				doc = commentText(field.Doc)
			} else if field.Comment != nil {
				doc = commentText(field.Comment)
			}
			out = append(out, renderedField{
				Name:     id.Name,
				Clr:      clr,
				Json:     jsonName,
				Optional: optional,
				Doc:      doc,
			})
		}
	}
	return out, nil
}

// renderedField is one property of a rendered record.
type renderedField struct {
	Name     string
	Clr      string
	Json     string
	Optional bool
	Doc      string
}

// clrType renders a Go field type as the C# type that reads it.
func (r *renderer) clrType(owner string, expr ast.Expr) (string, error) {
	switch t := expr.(type) {
	case *ast.Ident:
		if clr, ok := clrNames[t.Name]; ok {
			return clr, nil
		}
		return r.renderType(ownerPkg(owner) + "." + t.Name)

	case *ast.StarExpr:
		inner, err := r.clrType(owner, t.X)
		if err != nil {
			return "", err
		}
		return nullable(inner), nil

	case *ast.ArrayType:
		inner, err := r.clrType(owner, t.Elt)
		if err != nil {
			return "", err
		}
		return "List<" + inner + ">", nil

	case *ast.MapType:
		key, err := r.clrType(owner, t.Key)
		if err != nil {
			return "", err
		}
		value, err := r.clrType(owner, t.Value)
		if err != nil {
			return "", err
		}
		return "Dictionary<" + key + ", " + value + ">", nil

	case *ast.SelectorExpr:
		pkg, ok := t.X.(*ast.Ident)
		if !ok {
			return "", fmt.Errorf("unsupported selector")
		}
		if pkg.Name == "json" && t.Sel.Name == "RawMessage" {
			// Raw JSON rides through untouched: the shell hands it back to the
			// kernel rather than reading it.
			return "JsonElement", nil
		}
		if pkg.Name == "time" && t.Sel.Name == "Time" {
			// A Go time serialises as an RFC 3339 string, so the shell reads it
			// as one rather than carrying a clock type of its own.
			return "string", nil
		}
		dir, ok := packageAliases[pkg.Name]
		if !ok {
			return "", fmt.Errorf("package %q is not declared a wire source", pkg.Name)
		}
		for name, target := range packageAliases {
			if target == dir {
				return r.renderType(name + "." + t.Sel.Name)
			}
		}
		return "", fmt.Errorf("package %q has no alias", pkg.Name)

	default:
		return "", fmt.Errorf("unsupported field type %T", expr)
	}
}

// ownerPkg returns the package alias from a "alias.Type" key.
func ownerPkg(key string) string {
	if at := strings.Index(key, "."); at >= 0 {
		return key[:at]
	}
	return ""
}

// clrName renders a declared type's C# name.
func clrName(key string) string {
	name := key
	if at := strings.Index(key, "."); at >= 0 {
		name = key[at+1:]
	}
	// Two packages declare a Node with different fields, so the surface's is
	// qualified rather than colliding with the graph's.
	if name == "Node" && strings.HasPrefix(key, "surfacespec.") {
		return "SurfaceNode"
	}
	return name
}

// nullable renders a value type as its nullable form. A reference type stays as
// it is, because a missing string is already null.
func nullable(clr string) string {
	if strings.HasSuffix(clr, "?") {
		return clr
	}
	if valueTypes[clr] {
		return clr + "?"
	}
	return clr
}

// wireName reads the JSON name, whether the field serialises, and whether the
// payload may omit it.
func wireName(field *ast.Field) (string, bool, bool) {
	if field.Tag == nil {
		return "", false, false
	}
	tag, err := strconv.Unquote(field.Tag.Value)
	if err != nil {
		return "", false, false
	}
	raw, ok := lookupTag(tag, "json")
	if !ok || raw == "" || raw == "-" {
		return "", false, false
	}
	name, rest, _ := strings.Cut(raw, ",")
	if name == "" {
		return "", false, false
	}
	return name, true, strings.Contains(","+rest+",", ",omitempty,")
}

// lookupTag returns one struct tag's value.
func lookupTag(tag, key string) (string, bool) {
	for tag != "" {
		tag = strings.TrimLeft(tag, " ")
		at := strings.Index(tag, ":")
		if at <= 0 {
			return "", false
		}
		k := tag[:at]
		tag = tag[at+1:]
		if tag == "" || tag[0] != '"' {
			return "", false
		}
		quoted, rest, ok := cutQuoted(tag)
		if !ok {
			return "", false
		}
		tag = rest
		if k != key {
			continue
		}
		value, err := strconv.Unquote(quoted)
		if err != nil {
			return "", false
		}
		return value, true
	}
	return "", false
}

// cutQuoted splits a leading Go string literal from the rest of a struct tag.
func cutQuoted(s string) (string, string, bool) {
	for i := 1; i < len(s); i++ {
		if s[i] == '\\' {
			i++
			continue
		}
		if s[i] == '"' {
			return s[:i+1], s[i+1:], true
		}
	}
	return "", "", false
}

// toCRLF returns text with line endings normalised to CRLF.
func toCRLF(text string) string {
	unified := strings.ReplaceAll(text, "\r\n", "\n")
	return strings.ReplaceAll(unified, "\n", "\r\n")
}

// emitRecord writes one C# record with its documented properties.
func emitRecord(name, doc string, fields []renderedField) string {
	var b strings.Builder
	b.WriteString("// Code generated by tools/wiregen. DO NOT EDIT.\n\n")
	b.WriteString("using System.Collections.Generic;\n")
	b.WriteString("using System.Text.Json;\n")
	b.WriteString("using System.Text.Json.Serialization;\n\n")
	b.WriteString("namespace Reasonix.Studio.Contracts.Wire;\n\n")
	writeSummary(&b, doc)
	b.WriteString("public sealed record ")
	b.WriteString(name)
	b.WriteString("\n{\n")

	for i, field := range fields {
		if i > 0 {
			b.WriteString("\n")
		}
		// The summary is the Go author's own sentence when there is one. A field
		// with none says what it carries from its JSON name rather than repeating
		// the property name alone, which satisfies the checker and tells nobody
		// anything.
		summary := field.Doc
		if summary == "" {
			summary = fmt.Sprintf("%s carries the kernel's %q value.", field.Name, field.Json)
		}
		fmt.Fprintf(&b, "    /// <summary>\n    /// %s\n    /// </summary>\n", xmlEscape(summary))
		fmt.Fprintf(&b, "    [JsonPropertyName(\"%s\")]\n", field.Json)
		if field.Optional {
			b.WriteString("    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]\n")
		}
		fmt.Fprintf(&b, "    public %s %s { get; init; }\n", field.Clr, field.Name)
	}

	b.WriteString("}\n")
	return b.String()
}

// emitAlias writes a C# wrapper for a Go string type with a closed value set.
func emitAlias(name, doc string) string {
	var b strings.Builder
	b.WriteString("// Code generated by tools/wiregen. DO NOT EDIT.\n\n")
	b.WriteString("namespace Reasonix.Studio.Contracts.Wire;\n\n")
	writeSummary(&b, doc)
	b.WriteString("/// <remarks>The kernel sends this as a plain string.</remarks>\n")
	fmt.Fprintf(&b, "public readonly record struct %s(string Value)\n{\n", name)
	b.WriteString("    /// <summary>Reads the kernel's string form.</summary>\n")
	b.WriteString("    /// <param name=\"value\">The string the payload carried.</param>\n")
	b.WriteString("    /// <returns>The wrapped value.</returns>\n")
	fmt.Fprintf(&b, "    public static %s FromWire(string value)\n    {\n        return new %s(value);\n    }\n", name, name)
	b.WriteString("}\n")
	return b.String()
}

// writeSummary writes a type's summary, falling back to a sentence so the
// documentation law holds even for a type the Go source left uncommented.
func writeSummary(b *strings.Builder, doc string) {
	if doc == "" {
		doc = "Mirrors a type the kernel sends."
	}
	fmt.Fprintf(b, "/// <summary>\n/// %s\n/// </summary>\n", xmlEscape(doc))
}

// xmlEscape replaces the characters that would end a documentation element.
func xmlEscape(text string) string {
	replacer := strings.NewReplacer("&", "&amp;", "<", "&lt;", ">", "&gt;")
	return replacer.Replace(text)
}
