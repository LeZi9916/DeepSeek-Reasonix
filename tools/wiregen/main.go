// Command wiregen renders the kernel's wire contract as C# records.
//
// Run with no flags to write the files, or -check to compare without writing.
// The renderer itself lives in internal/base/wiregen so tools/repolint can hold
// the committed files to a fresh render; this command only decides what to do
// with the result.
package main

import (
	"flag"
	"fmt"
	"log"
	"os"
	"path/filepath"
	"sort"

	"reasonix/internal/base/wiregen"
)

func main() {
	root := flag.String("root", ".", "repository root")
	check := flag.Bool("check", false, "compare rendered files without writing them")
	flag.Parse()
	log.SetFlags(0)

	if *check {
		if err := wiregen.Check(*root); err != nil {
			log.Fatal(err)
		}
		fmt.Println("Wire records are up to date.")
		return
	}

	files, err := wiregen.Render(*root)
	if err != nil {
		log.Fatal(err)
	}

	dir := filepath.Join(*root, filepath.FromSlash(wiregen.OutputDir))
	if err := os.MkdirAll(dir, 0o755); err != nil {
		log.Fatal(err)
	}

	names := make([]string, 0, len(files))
	for name := range files {
		names = append(names, name)
	}
	sort.Strings(names)
	for _, name := range names {
		if err := os.WriteFile(filepath.Join(dir, name), []byte(files[name]), 0o644); err != nil {
			log.Fatal(err)
		}
		fmt.Println(filepath.ToSlash(filepath.Join(wiregen.OutputDir, name)))
	}
}
