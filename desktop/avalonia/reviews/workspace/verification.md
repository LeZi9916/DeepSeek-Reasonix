---
owner: @esengine
backup: @SivanCola
status: active
reviewed: 2026-11-22
---

# Workspace navigation review

Twenty pairs cover search, recent sessions, session menus, device access and community. Each surface is captured at 1440 × 900 and 760 × 480, in both themes. Electron is on the left. Eight additional sidebar pairs retain initial and remote scroll positions.

The reference is the existing Electron benchmark UI. Native captures use the HTTP responses exported by its `__nativeReference`, including the actual recent-tree and share responses. They do not replace production state with presentation fixtures.

Canonical sidebar behavior now includes:

- Bounded tree pagination, same-scope retry, search and local/remote windows.

- Six scopes, their available counts, explicit clear and archive separation.

- Pin marks, preference-controlled quick actions, branch and kept histories.

- Batch archive/delete with live-session guards and retained refused paths.

- Dynamic installed editors, remote placement and known-project move-to.


Recent history reads its own canonical offset and selected machine. It shows local-calendar groups, chat marks, activity tails and keyboard help. An acknowledged pick clears the scope, unfolds its project, reveals the actual row and restores its keyboard focus.

Search supports explicit scopes, best match, three recent selections and five results per kind. Additional commands open actual settings destinations. Wallet and usage opens the independent usage route.

Device access retains acknowledged LAN and cloud state. It shows the returned QR paths natively, withdraws a consumed or expired offer, lists devices and confirms disconnect. Canonical presence changes drive the compact online badge and short notice.

Community reuses the release's QQ, Douyin and contributor assets. `export-community-assets.mjs` reproduces the native QQ bitmap from the existing SVG. External links require an explicit activation; headless tests replace the browser callback.

The affected suite passed 63 cases. It covers navigation ordering, same-target pending acknowledgement, refusal retry, native row hit testing, recent selection, menus, scopes, devices, search and all eight capture theories.

Three cases use real Windows hosts with isolated profiles:

- Real imported histories, titles, bodies, draft isolation and actual HTTP 409 refusal.

- A local loopback provider producing a real delayed model stream while focus moves away and back.

- Rename, move, placement, folder addition and deletion persisted through kernel APIs.


No paid API or user credential is used. Required-host mode fails when its executable is missing. Geometry, coverage and session-age validation separately passed 14 cases. No parity debt was widened.

Final menu, keyboard and capture changes passed six further cases after strict compilation. Logs are `sidebar-final-suite.log`, `sidebar-visual-final.log` and `sidebar-gates-final.log` under `D:/TempFiles`.

Remote SSH authentication, internet account pairing and non-Windows accessibility were not exercised. The images retain rasterization and native-platform differences for review.