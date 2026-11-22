---
owner: @esengine
backup: @SivanCola
status: active
reviewed: 2026-11-22
---

# Workspace sidebar review

The four pairs capture the actual sidebar at 1440 × 900 and 760 × 480 in both themes. Each image retains the 264-pixel sidebar at its original scale: Electron on the left, Avalonia on the right. Compact captures open the sidebar through the normal rail toggle.

`ProjectRail.axaml`, `ProjectEntry.axaml` and `SessionEntry.axaml` own the native toolbar and rows. Project and conversation rows are 30 DIPs high with a one-DIP gap.

Folder and disclosure icons, indentation, title truncation, hidden session menus, selected rows and status marks use the reference geometry and semantic inks.

Typeface resources use the same static Inter and Noto faces as the workbench; saved interface-font preferences still override them.

The project toolbar provides recent sessions, project folding, local unread/pinned/archive scopes and the existing project folder picker.

Recent sessions come from the canonical tree in timestamp order. Pagination and individual folds survive tree refreshes. Pin, archive and mark-read actions retain the kernel-owned paths and acknowledgements.

The brand uses the wordmark's alpha mask: its original blue in light mode and 94% white in dark mode. Compact windows hide the STUDIO badge.

The footer restores the settings icon and opens an anchored shortcuts menu containing the existing account, model, extension and connection settings routes.

All eight sidebar cases pass. They cover pagination, folds, refresh, scopes, mark-read dispatch, account navigation, fonts and inks, plus both capture sizes and themes. A selected title resolves to 600 weight; hovering reserves space for its menu.

The solution builds with zero warnings and errors; all 280 baseline tests and 118 render tests pass.

The geometry gate earns five mapped identities: the project header, projects tab, recent tab, fold-all and add-project action. Their former missing entries are removed from the debt file and added to the coverage record. No new debt entry is added.

The comparison deliberately preserves the reference's remote-project rows. The native sidebar currently projects its local tree only; remote project discovery and activation are still absent from this view.

The native search field queries session/project titles; the reference search entry also opens an action palette. These are functional gaps, not screenshot fixture differences hidden by the capture.

Go vet passes and gofmt produces no Go changes. Go lint reports six existing findings outside this change. The builtin-tool tests pass.

WSL boot tests report unavailable bubblewrap isolation and time out after 600 seconds.

`make check` reports existing global budgets for CSS size (8883 > 8876), essays (2551 > 2538), release notes (679 > 661) and wire parity (78 > 0); it reports no path violation in this change.
