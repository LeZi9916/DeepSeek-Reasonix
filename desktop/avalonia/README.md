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

### Publishing a distributable shell

```sh
make studio-avalonia-publish RID=win-x64        # or linux-x64, osx-arm64, ...
bash scripts/build-studio-avalonia.sh --help    # all options
```

The command publishes the shell self-contained by default. It puts the CGO-free Go
host beside the shell so `HostLauncher.Locate` can find it at startup.

The script defaults to `desktop/avalonia/bin/publish/<RID>`. It accepts `--output`,
`--configuration`, `--single-file`, `--no-self-contained` and `--no-restore`.

Test projects declare `IsPublishable=false`, which excludes their artifacts when
publishing the solution.

### One-click test build

```sh
make studio-test-build RID=win-x64
bash scripts/build-studio-test.sh --help
```

The command puts a self-contained Avalonia shell and a cross-compiled Go host
in `desktop/avalonia/bin/test-build/<date>-<shortsha>/<platform>/`.

It also builds the solution and runs both test projects with the host alongside
their assemblies. The `verification/` directory holds the logs and
`validation.json`. Use `--skip-tests` to build without running the suites or
`--dir` to choose the output directory.

The workbench's **Browser** menu opens a manual system WebView. Workspace HTML, DOCX, XLSX, PDF, CSV and TSV files also offer previews. Delimited files use the native table, including while previewing a source draft.

After editing the document renderers, run `node desktop/frontend-next/tools/build-native-office.mjs` or `node desktop/frontend-next/tools/build-native-pdf.mjs` with the frontend dependencies installed. Both renderers are embedded in the Avalonia assembly so a .NET build can run independently of Node.

To check the actual adapter on a desktop, run `dotnet run --project desktop/avalonia/tests/Reasonix.Studio.WebView.Smoke` from the repository root.

The smoke test verifies browsing, isolated HTML previews, Office rendering, and PDF page navigation and zoom on the platform adapter. The Studio workflow exposes the same check through its `system_webview` manual input.

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

Open `desktop/avalonia/bin/parity/overview.html` to browse 74 layout comparisons by theme and window size. Each page title links to its full-size renders and measured differences.

Open `editors.html` for four additional screenshot pairs covering memory editing, revision history, preset creation and duplication.

The React capture also exports `page-inputs.json`. The native tests replay those same responses from their checked-in fixture. Refresh the fixture alongside an intentional reference change.

The [data settings review](reviews/settings-storage/report.json) records the measured geometries for all four size/theme combinations. Its adjacent PNGs show Electron on the left and Avalonia on the right.

The Windows review uses the embedded path font for the session write-location row. All 77 mapped nodes have no box differences at the 0.5-DIP tolerance in both sizes and themes, including wrapping at 760 × 480. Unmapped nodes and style differences remain listed in the report.

To regenerate a page review after running the native render tests, run `node tools/baseline/review-page.mjs settings-storage` from `desktop/frontend-next` with the reference harness running.

The [workbench review](reviews/workbench/report.json) distinguishes Git and workbench geometry from the conversation visible beside it. Each viewport includes the explorer, expanded changes and an unconfirmed commit proposal. Open the [page review gallery](reviews/index.html) to switch between them.

The [conversation review](reviews/conversation/verification.md) adds twelve pairs for settled messages, reasoning/work expansion and a focused draft, in both themes and window sizes.

The [sidebar review](reviews/sidebar/verification.md) covers the workspace rail separately. Both are available in the page review gallery.

### Parity checks and remaining verification

| Area | Next implementation and acceptance check |
| --- | --- |
| Data and agent settings | Native controls exercise memory edits, history, restoration, confirmation and archive. A real isolated host verifies the revision chain, preset copy, stale-revision refusal and new-task binding. The file picker previews imported definitions before saving. Composer proposals can be accepted, edited or dismissed. |
| Appearance and extensions | Installed packs now supply background images, sky, surface opacity and per-scheme colors. Font, reading size, UI scale and reviewed marketplace updates have interaction tests. Compare OS-specific fonts and compositing on each desktop. |
| Connections and onboarding | The connection settings manage saved machines. Onboarding supports ambiguous protocols, a folder picker, folder drop and skip. A real-host test verifies that the selected root is used by the next runtime. |
| Workbench and analysis | File-tab overflow, selectable unified diffs, grouped signal inspection and 1–16× zoom have interaction tests. Keep browser acceptance on the manual system WebView path. |
| Platform interaction | Run the Windows and Linux CI jobs and inspect their screenshots. Verify keyboard focus, native menus, scrolling, motion and the system WebView on each desktop. |

## What the geometry baseline is

`baseline/` holds the boxes, inks, type scale and spacing the reference
frontend actually resolved, captured per page domain and per scheme. The gate compares these JSON measurements across engines and platforms. Screenshots support review of glyph rendering and antialiasing.
