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

The geometry gate checks the mapped workbench shell and Git action identities at a 0.5-DIP tolerance. Their box difference count is zero. The full-window report retains 10 desktop or 13 compact box differences in the adjacent conversation and chrome. Unmapped nodes and style differences are still outstanding.

Go vet passes. Go lint reports six existing findings outside this change. `make check` reports existing repository totals for CSS size, essays, release notes and wire parity.

WSL boot tests cannot complete with the required shell sandbox unavailable; builtin tool tests pass.

The conversation beside Git now uses the native AXAML message surfaces described in [the conversation review](../conversation/verification.md). All sixteen pairs were regenerated after the conversation and sidebar changes.
