---
owner: @esengine
backup: @SivanCola
status: active
reviewed: 2026-11-22
---

# Workbench and Git review

The pairs cover the explorer, file tree with Git sections folded, expanded changes and unconfirmed commit review at 1440 × 900 and 760 × 480 in light and dark themes. Electron is on the left, Avalonia on the right.

`GitChangesView.axaml` separates commands, review and scrollable patches. Status refreshes preserve file folds. Closing the Git tab restores an unsaved file in the editor. The shared button styles retain accent contrast on hover and provide a keyboard focus adorner.

`WorkspaceFileRow.axaml` matches the reference's 30-DIP rows, folder disclosure, file icons, 14-DIP nesting and neutral selection. Expanded folders survive refreshes; search results include their parents. Git badges use the deepest owning repository's status.

`GitStageBox` uses a shared three-state checkbox theme: 16-DIP explorer boxes and 14-DIP board boxes, with centered vector checks and mixed-state bars. Git readback owns the displayed state. Accessibility activation follows the same staging command as pointer and keyboard activation.

Workbench typography uses static Inter, Noto and IBM Plex faces derived from the frontend assets by `render-workbench-fonts.py`. Row names use 500 weight, metadata uses 600 and section headings use 700. Saved interface and code fonts override these resources.

Sidebar Git statistics use the reference IO colors: blue for insertions and pink for deletions. Names, folder counts, hashes, timestamps and branch labels use their captured semantic inks in each theme. Patch lines keep the addition and deletion palette.

The native render suite exercises all four size/theme combinations, commit acknowledgement, cancellation, push preview, index changes, history and repository selection.

A real Windows host exercises an isolated nested Git repository, including branch switching and restoration with a repository-local line-ending policy.

The geometry gate checks the mapped workbench shell and Git action identities at a 0.5-DIP tolerance. Their box difference count is zero. The full-window report retains differences in the adjacent conversation and chrome, listed in report.json.

Unmapped nodes and style differences are still outstanding.

Go vet passes. Go lint reports six existing findings outside this change. `make check` reports existing repository totals for CSS size, essays, release notes and wire parity.

WSL boot tests cannot complete with the required shell sandbox unavailable; the builtin suite also fails on the existing Git metadata denial attribution fixture.

The conversation beside Git now uses the native AXAML message surfaces described in [the conversation review](../conversation/verification.md). All sixteen pairs were regenerated after the conversation and sidebar changes.

All sixteen pairs include the aligned composer controls and telemetry footer; its interaction validation is recorded in the conversation review.


The plus button and empty workbench now expose files, browser, terminal and recent paths through a native launcher. Explorer actions open the configured editor, select the workspace in its file manager and request hidden listings from the kernel.

File menus restore canonical references, copied paths and platform actions. Reveal and Open use distinct native verbs and await the operating system acknowledgment. Refused or missing paths remain visible failures.

Browser tabs retain independent guests and close without losing file drafts. Workbench presentation is owned per runtime; same-runtime conversation rebinding preserves the project editor buffer, folds and tabs. Late terminal responses are checked against the owning engine.

Actual Windows WebView2 testing exposed a missing supported-OS manifest and a rejected generated HTML address. The shared manifest now declares Windows and per-monitor DPI support; only the exact explicit generated document bypasses ordinary address validation.

The desktop smoke now passes address navigation, Back, Forward, Reload, rejected file URLs and the sandboxed workspace iframe at the machine’s 150% scaling. Scaled headless checks cover the metric geometry at 125%, 150% and 200%.

The Git interaction regression reopens the actual workbench to refresh canonical repository state. It then checks stage/unstage, discard cancellation/confirmation, reviewed commit fingerprints and secret acknowledgment, push preview/cancel/confirm and disabled zero-ahead state.

The production entry now declares STA. A separate native experiment with identical pages failed before loading on MTA and passed on STA; the production entry attribute is covered by a regression test.

Native two-tab testing also reproduced discarded browser history after switching tabs. StudioSurfaceHost now keeps inactive guests attached and hidden; explicit tab or runtime closure releases them. The same native test now retains Back/Forward history across tab changes.
