---
owner: @esengine
backup: @SivanCola
status: active
reviewed: 2026-11-21
---

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
- For manual web previews, use the system WebView. Windows requires the Edge WebView2 runtime. macOS uses WebKit. Linux requires WebKitGTK or WPE WebKit and its system libraries.
- **Go**, matching `go.mod`'s `toolchain` line, for the kernel-side gates.

## Building and testing

```
make studio-dotnet        # build the solution
make studio-dotnet-test   # run its cases
make studio-native        # build the Go host beside the shell and launch the native window
```

The CI matrix builds the same solution in Release and runs both test projects with the compiled Go host.

Warnings are errors, and `GenerateDocumentationFile` is on: a member without
XML documentation fails the build rather than reaching review.

The workbench's **Browser** menu opens a manual system WebView. A workspace HTML file also offers a preview.

To check the actual adapter on a desktop, run `dotnet run --project desktop/avalonia/tests/Reasonix.Studio.WebView.Smoke` from the repository root.

The smoke test serves two local pages, opens them through the workbench, and verifies navigation, history, reload and isolated HTML preview. The Studio workflow exposes the same check through its `system_webview` manual input.

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
every git command fails with `not a git repository`.

Repointing `GIT_DIR` at the
`/mnt/c` form fixes the lookup — but a `git status` or `git diff` across the
Windows drive then climbs the whole tree through 9p and can take minutes.

For this setup, `make check` (repolint `-changed`, which shells out to git) is not a
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

## Comparing the desktop renderers

The `shell (dotnet)` CI matrix runs on Windows, macOS and Linux. Each job builds the real Go host and uploads native screenshots and difference reports. The conversation gate enforces the recorded differences.

The named-screen tests use the normal controls to enter 18 distinct page states at 1440 × 900 and 760 × 480 in both themes, plus the conversation at 1000 × 720 in both themes.

These 74 comparisons check page-specific geometry anchors, including settings search and navigation, the workbench explorer and canvas, the onboarding card, and the analysis header and metrics. Their reports also include the missing identities and outstanding differences across the rest of each page.

To generate the native side locally, build the solution and run the capture from the repository root:

```sh
make studio-dotnet
STUDIO_PARITY_OUTPUT="$PWD/desktop/avalonia/bin/parity/native" \
  dotnet test desktop/avalonia/Reasonix.Studio.slnx --no-build --filter FullyQualifiedName~NativeDomainTests
```

To capture the React side, serve the production `perf.html` bundle following the frontend performance harness instructions. From `desktop/frontend-next`, with Playwright Chromium installed:

```sh
STUDIO_PARITY_SHOTS="$PWD/../avalonia/bin/parity/reference" \
  STUDIO_BASELINE_OUT="$PWD/../avalonia/bin/parity/geometry" \
  PERF_CHANNEL=bundled node tools/baseline/collect.mjs
PERF_CHANNEL=bundled node tools/baseline/capture-settings-flows.mjs
node ../avalonia/tools/compare-pages.mjs
```

Open `desktop/avalonia/bin/parity/overview.html` to browse the full set by theme and window size. Its page titles lead to the paired full-size renders and the node, geometry and style findings in `index.html`.

The React capture also exports `page-inputs.json`. The native tests replay those same responses from their checked-in fixture. Refresh the fixture alongside an intentional reference change.

### Remaining parity work

| Area | Next implementation and acceptance check |
| --- | --- |
| Data and agent settings | Memory editing, historical restoration and version-aware preset creation, duplication and editing have ordinary-control interaction tests. Compare the remaining library import/export and memory suggestion flows. |
| Appearance and extensions | Verify illustrated theme backgrounds and typography controls. Marketplace installation and updates now display and apply the kernel's reviewed plan. |
| Connections and onboarding | Complete remote-machine management and the remaining protocol and project-selection steps. Exercise connection failure, retry and back navigation. |
| Workbench and analysis | File-tab overflow, selectable unified diffs, grouped signal inspection and 1–16× zoom have interaction tests. Keep browser acceptance on the manual system WebView path. |
| Platform interaction | Run the Windows and Linux CI jobs and inspect their screenshots. Verify keyboard focus, native menus, scrolling, motion and the system WebView on each desktop. |

## What the geometry baseline is

`baseline/` holds the boxes, inks, type scale and spacing the reference
frontend actually resolved, captured per page domain and per scheme. The gate compares these JSON measurements across engines and platforms. Screenshots support review of glyph rendering and antialiasing.
