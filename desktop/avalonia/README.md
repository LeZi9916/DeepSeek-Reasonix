# Reasonix Studio, the native shell

The replacement desktop shell: Avalonia drawing Reasonix's own interface in
process, over the same HTTP + SSE surface `internal/frontend/serve` gives every
frontend. It owns presentation and OS capability; the kernel owns all durable
state.

```
┌──────────────────────────────────────────────────────────────┐
│  Reasonix.Studio.Shell  (AXAML + controls, this process)      │
│    Pages · Controls · State · Kernel client                   │
└───────────────▲────────────────────────────┬─────────────────┘
                │ HTTP + SSE over 127.0.0.1  │
┌───────────────┴────────────────────────────▼─────────────────┐
│  cmd/reasonix-studio-host  (main module, CGO-free)            │
└───────────────────────────────────────────────────────────────┘
```

## Layout

| Path | What it is |
| --- | --- |
| `src/Reasonix.Studio.Contracts` | wire records generated from the kernel's Go structs |
| `src/Reasonix.Studio.Kernel` | HTTP + SSE client, the loopback boundary, one conversation end to end |
| `src/Reasonix.Studio.State` | the immutable reducer: frames in, one transcript out |
| `src/Reasonix.Studio.Controls` | the shell's own controls and their control themes |
| `src/Reasonix.Studio.Pages` | conversation and pane pages |
| `src/Reasonix.Studio.Shell` | the window, `IHostPort`, launch and lifetime |
| `tools/Reasonix.Studio.Baseline` | geometry exporter and comparer |
| `tools/render-tokens.mjs` | tokens captured from the reference frontend, rendered as resources |
| `baseline/` | the captured geometry every page is held to |
| `tests/Reasonix.Studio.Baseline.Tests` | the shell's cases, including the real kernel |

`BOUNDARY.md` records the loopback half this shell owes; `HOST_PORT.md` is the
capability ledger. Both state what is and is not covered.

## Prerequisites

- **.NET SDK 10 or newer.** The projects target `net10.0`: that is the newest
  target Avalonia 12.1.6 publishes assets for, so a newer SDK builds them
  without a `net11.0` runtime to run on.
- **Node 24+**, for `tools/render-tokens.mjs` and the baseline collector.
- **Go**, matching `go.mod`'s `toolchain` line, for the kernel-side gates.

## Building and testing

```
make studio-dotnet        # build the solution
make studio-dotnet-test   # run its cases
```

Both targets are what CI's `shell (dotnet)` job runs, so a local run and CI
answer the same question.

Warnings are errors, and `GenerateDocumentationFile` is on: a member without
XML documentation fails the build rather than reaching review.

## Running the kernel-side gates

`go build`, `go vet`, `wiregen -check` and `repolint` all read the Go tree, so
they run wherever a Go toolchain is. On a Windows host without Go, a WSL
distribution works — but two platform facts have to be accounted for, because
each one produces a red result that has nothing to do with the code under test.

**The toolchain is often installed but not on `PATH`.** Check before installing
another one:

```
wsl.exe -e bash -lc 'ls /home/*/sdk/go/bin /usr/local/go/bin 2>/dev/null'
```

**A worktree's `.git` file names an absolute Windows path.** WSL's git reads
`gitdir: C:/…` as a relative path and joins it onto the working directory, so
every git command fails with `not a git repository`. Repointing `GIT_DIR` at the
`/mnt/c` form fixes the lookup — but a `git status` or `git diff` across the
Windows drive then climbs the whole tree through 9p and can take minutes, which
is why `make check` (repolint `-changed`, which shells out to git) is not a
usable form here.

The working division is therefore: **let the Windows-side git name the change
set, and let WSL scan it.** `repolint` takes exactly that list through `-only`:

```
git ls-files --others --exclude-standard          # in Windows
git diff --name-only --diff-filter=ACMRTUXB HEAD  # in Windows
wsl.exe -e bash -lc 'PATH=/home/<you>/sdk/go/bin:$PATH \
  go run ./tools/repolint -only <comma-separated paths>'
```

Two things this does not do, and both matter:

- It is not `make check`. The host records a `make` target as a check whose
  result it can read; an arbitrary `go run ./tools/...` is a program it must
  assume writes. Cite `-only` as a local scan, not as evidence that the
  standards gate passed.
- It covers only the paths a person listed. A file left out is a file the gate
  never opened, which is the failure `-changed` exists to prevent.

On a host with a native Go toolchain, `make check` runs as written and none of
this applies. CI is that host.

## What the geometry baseline is

`baseline/` holds the boxes, inks, type scale and spacing the reference
frontend actually resolved, captured per page domain and per scheme. It is JSON
rather than screenshots because glyph rasterisation and antialiasing differ per
engine and per platform, and a gate failing on those would be switched off
within a week. What it holds the shell to is what both sides can compute.
