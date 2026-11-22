---
owner: @esengine
backup: @SivanCola
status: active
reviewed: 2026-11-22
---

# Workspace sidebar review

Eight pairs capture the native and Electron sidebars at 1440 × 900 and 760 × 480 in both themes. Each retains the 264-pixel rail at its original scale. The second position shows the remote projects and connection progress.

Project and session rows are 30 DIPs high with a one-DIP gap. Fonts use the same bundled Inter and Noto faces. Semantic resources supply light and dark inks; saved interface-font preferences still override the default.

The default preview retains five rows and the current conversation. More both expands loaded previews and requests the next bounded canonical window. Non-default scopes show their complete loaded matching population. At 300 rows the caption points to search.

The six scopes use real unread, pin, archive and reducer-owned activity state. Archive has no misleading active-window count. Clear, same-scope retry and per-machine paging preserve canonical ownership.

Menus, quick actions, branch, kept histories, multi-selection and dynamic installed editors reuse kernel APIs. Move-to selects an acknowledged project and preserves a partial result. MRU search entries retain canonical identities.

The rail reads `/remotes` and each machine's tree. It retains usable rows after transient errors and rejects older queries. Machine labels, connection progress and bootstrap steps remain beside the correct project.

Session age comes from `lastActiveAt`. An open runtime alone does not imply an active turn. Runtime machine identity also scopes drafts when multiple machines use the same path.

Real pointer coverage includes titles, blank row space, timestamps, native double-click rename and keyboard activation. A canonical refresh between press and release preserves the pressed target. Late navigation cannot replace a later row or tab selection.

Recent selection clears its previous scope, unfolds the acknowledged project, scrolls the selected row into view and restores focus. Its own canonical search supports machine selection, pagination and retry.

The affected suite passed 63 tests, including real Windows navigation, real background model streaming through a loopback provider, gestures and eight capture theories. Geometry, coverage and age validation passed 14 separate cases.

See `../workspace/verification.md` for reproducible capture tools and validation boundaries. Internet pairing, SSH credential prompts and non-Windows platform accessibility remain unmeasured.