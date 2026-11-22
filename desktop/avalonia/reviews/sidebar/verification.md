---
owner: @esengine
backup: @SivanCola
status: active
reviewed: 2026-11-22
---

# Workspace sidebar review

The eight pairs capture the actual sidebar at 1440 × 900 and 760 × 480 in both themes, at its initial position and scrolled to the remote projects. Each image retains the 264-pixel sidebar at its original scale: Electron on the left, Avalonia on the right.

Compact captures open the sidebar through the normal rail toggle.

`ProjectRail.axaml`, `ProjectEntry.axaml` and `SessionEntry.axaml` own the native toolbar and rows. Project and conversation rows are 30 DIPs high with a one-DIP gap.

Folder and disclosure icons, indentation, title truncation, hidden session menus, selected rows and status marks use the reference geometry and semantic inks.

Typeface resources use the same static Inter and Noto faces as the workbench; saved interface-font preferences still override them.

The project toolbar provides recent sessions, project folding, unread/pinned/archive scopes and the existing project folder picker. These projections include both local and remote session books.

Recent sessions come from the canonical tree in timestamp order. Pagination and individual folds survive tree refreshes. Pin, archive and mark-read actions retain the kernel-owned paths and acknowledgements.

The brand uses the wordmark's alpha mask: its original blue in light mode and 94% white in dark mode. Compact windows hide the STUDIO badge.

The footer restores the settings icon and opens an anchored shortcuts menu containing the existing account, model, extension and connection settings routes.

All eleven sidebar render cases pass. They cover pagination, folds, refresh, scopes, host-scoped pin dispatch, mark-read dispatch, account navigation, fonts and inks, actual running/failure/settled transitions, stale remote reads, transient failure and recovery, plus both capture sizes and themes.

A selected title resolves to 600 weight; hovering reserves space for its menu and hides the timestamp.

The affected render project builds without compiler warnings or errors. The session-age boundary test passes for minute, hour, yesterday, calendar-date and unknown timestamp cases.

The geometry gate earns five mapped identities: the project header, projects tab, recent tab, fold-all and add-project action. Their former missing entries are removed from the debt file and added to the coverage record. No new debt entry is added.

The sidebar reads the real `/remotes` address book and each connected machine's `/remotes/{host}/tree` response. It merges acknowledged projects with saved workspace paths, retains the last acknowledged tree through a failed read, and rejects responses belonging to an older query or engine.

A three-second refresh projects connection progress independently of transcript traffic.

Machine labels, connecting/offline state and bootstrap steps now remain visible beside the correct project. The fixture therefore shows training, scratch, eval and notes on gpu-box, and work on builder with the installing-reasonix step.

Remote opening reuses the existing runtime switch and validates the returned same-origin runtime path; row mutations write to the owning host and re-read its book.

Session age comes from canonical `lastActiveAt`. The active conversation's reducer supplies running and needs-confirmation marks; an open runtime alone no longer claims that a turn is running.

Remote runtime descriptors also retain their machine name for draft isolation when two machines use identical paths.

The captures and tests use the authenticated HTTP fixture rather than a live SSH machine. Remote credential prompts and state subscriptions for additional simultaneously running panes were not exercised locally.

The native search field queries session/project titles; the reference search entry also opens an action palette. These are functional gaps, not screenshot fixture differences hidden by the capture.

Go vet passes and gofmt produces no Go changes. Go lint reports six existing findings outside this change.

The builtin-tool suite fails in the existing Git metadata denial attribution fixture.

WSL boot tests report unavailable bubblewrap isolation and time out after 60 seconds.

`make check` reports existing global budgets for CSS size (8883 > 8876), essays (2551 > 2538), release notes (679 > 661) and wire parity (78 > 0); it reports no path violation in this change.
