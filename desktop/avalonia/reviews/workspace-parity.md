---
owner: @esengine
backup: @SivanCola
status: active
reviewed: 2026-11-22
---

# Desktop workspace parity ledger

The reference is the checked-in `frontend-next` application, including its final design-layer overrides. A label or automation identity alone is not proof of an operation. The table names the canonical result and the interaction evidence.

| Reference element and condition | Native surface | Operation and state boundary | Evidence |
| --- | --- | --- | --- |
| Local or remote session row | `ProjectRail`, `SessionEntry` | Complete machine/root/path target; native mouse release and Enter share navigation | `ARealSessionClickOpensItsWorkspaceAndRestoresItsDraft`, `RuntimeNavigationReadsRealWindowsSessionsAndKeepsDrafts` |
| A tree refresh during a press | `ProjectRail.Pointer` | Retain the native pressed target until release, then project the latest tree | The same pointer case with `refreshWhilePressed=true` |
| Repeated or rapid session selection | `WindowSessionCoordinator` | Share pending Hub opens; latest navigation alone focuses; refusal retains the previous body/draft | `SidebarRepeatedPendingOpenWaitsForAcknowledgement`, `SidebarLateOpenCannotTakeFocusFromLaterClick`, `SidebarRefusedOpenCanRetryOnNextClick` |
| First and subsequent session opens | `SessionEngine`, coordinator | Hub descriptors own runtime routing; `/resume` only reuses an eligible local runtime in the same project | `RuntimeRoutingTests`, `PaneLimitsPreserveRunningWorkAndReuseAnIdleLocalProject` |
| Background running session | Retained `SessionPaneState` | Focus changes preserve SSE, generation, drafts and queue ownership | `RuntimeSidebarKeepsRealStreamingTurnAliveAcrossFocus`: actual Windows host and delayed loopback provider |
| Earlier history and retry | `SessionPage.TopContent`, `EarlierHistory` | `/history/page` snapshot/cursor; tool restoration; retained live rows and draft; visible retry | `HistoryPagingTests`, `RealHistoryPagesPrependWithoutLosingTheDraftOrDuplicatingRows` |
| SSE reconnect and replay | `EventSubscription`, `SseEventTransport` | Reconnect backoff, gap refresh, replay provenance; no synthetic live speed | `SseReconnectTests`, `GenerationTelemetryTests` |
| Occupied or oversized history | Visible refusal and explicit tail offer | Refusal identity remains canonical; recent-tail copy requires an explicit operation | Real-host occupancy case and existing session boundary/tail tests |
| Session tab and tab menu | `SessionTabs` | Focus/close/rename target the retained pane; arrows/Home/End; running/unread marks | `SessionTabsDispatchNativeFocusCloseRenameAndMenus`, retained-tab navigation-order case |
| Explicit open-tab limit | Local controls preference | Zero disables folding; running panes survive; settled local panes may be reused | `PaneLimitsPreserveRunningWorkAndReuseAnIdleLocalProject` |
| Global search and Ctrl+K | `WorkspaceSearch` | Prefix scopes, recent entries, arrows/Enter/Escape, command dispatch and focus restoration | `WorkspacePaletteScopesAndKeysDispatchSelectedTarget`, palette order/destination tests |
| Project menu and folder drop | `ProjectRail.ProjectMenus` and gestures | Existing Hub add/remove/place/editor operations; missing and remote capabilities disable actions | Project-menu, native-folder-drop and real-host gesture tests |
| Session right-click and quick actions | `ProjectRail.Menus` | Rename, pin/archive/viewed, copy/version/import/export, move and batch operations use canonical APIs | Sidebar menu/batch/move tests; real-host rename/move/delete persistence |
| Inline rename | `SessionEntry.Gestures` | Native double click; trim changed title; Enter/blur save; Escape/empty/unchanged do not write; refresh retains draft | Sidebar double-click cases, including quick-action overlap bounds |
| Recent history | `RecentSessionsPanel` | Canonical recent query; machine/search/page/retry; acknowledged choice clears scope and locates the row | Recent location and paging tests |
| Remote and worktree chrome | `WorkspaceChromeStatus`, responsive chrome | Actual host, isolated workspace and queue readings; compact menu preserves controls | Sidebar status, palette and four-view captures |
| Device access | `DeviceAccessPanel` | `/share` readback, explicit toggle/address/disconnect, QR lifetime and device presence | Device access/cloud/expiry/presence tests |
| Message actions | Native message cards and find bar | Original Markdown copy, selected quotation, branch, find, tool output paging | Existing message, conversation and full-output regression suites |
| Attachments and task controls | Native composer controls | Intake, mode, permission, model, effort and branch use existing kernel admission/readback | Existing composer transport/intake/mode and native composer-element tests |
| Model and reasoning picker | `ModelPicker`, `MainWindow.ModelPicker` | Search, provider chips, grouped accounts, current row, vision badge, manage footer, effort ladder, declared model modes; model/effort/mode writes re-read status; a model pick keeps the panel open; provider chip filters and provider ordering are saved in workspace preferences and the model settings card moves providers with arrows; optional provider health readings appear as muted/warning badges | `ModelPickerSearchesAndCommitsChoices`, `ModelPickerPersistsProviderFilter`, `ModelPickerShowsProviderHealthWhenEnabled` |
| Work mode and execution permission panels | `ModePanel`, `PolicyPanel`, `ComposerPopup` | Self-drawn panels replace system menus; header, descriptions, current check, high-risk copy, sandbox entry, in-panel refusal and pending state; reference geometry and both palettes compared | `ComposerModesReadBackWithoutReplacingDraft` |
| Local branch palette | `BranchPanel`, `MainWindow.ComposeBranches` | Searchable local branches, current and other-worktree markers, retry state, immediate switch and readback; trigger disabled while a turn runs | `ComposerBranchPaletteSearchesAndSwitches` |
| Explorer count and width | Native workbench chrome | Visible node count and a drag handle persisted in workspace preferences; double click restores the reference width | `WorkbenchExplorerShowsCountAndDefaultWidth` |
| File editor text actions | `FileEditor` context menu | Quote selection becomes a quote chip whose reference block rides in the submit text; cut/copy/paste/select-all/undo/redo keep selection and editability state | `WorkbenchSelectionQuotesIntoTheComposer`, `QuoteChipTravelsInTheSubmitText` |
| Granted-file read-only state | File bar badge | Names the reason beside the file bar and keeps save disabled | Granted-root read path and visible badge state |
| File bar identity and save action | `Breadcrumb`, `SaveFile` | Muted directories with a bold filename, and the reference check-plus-Save affordance; both dark and light crops compared | `workbench.dark` / `workbench.light` render pairs |
| Git inventory recovery | `GitRetry` | A failed repository read keeps its message and offers the same read again; success clears both | `WorkbenchGitInventoryFailureOffersRetry` |
| Hidden-file preference | `HiddenFiles` | The explorer's hidden-file choice is device-local and survives the next launch | `HiddenFiles` preference round trip |
| File read recovery | `FileErrorPanel` | A refused read stays in the canvas with Read again and Close tab; the file bar title is no longer replaced by the error | `WorkbenchFileReadFailureOffersRecovery` |
| Browser tab menu | `MainWindow.SurfaceTabs` | Header plus close current, others, right and all; file tabs deliberately keep the file menu because the reference has no file-tab close menu | Browser/workbench action tests |
| Attachment chips | `AttachmentStrip`, `MainWindow.Attachments` | Dropped references and long pastes sit above the prompt with a file mark and remove action; refused drops keep their reason and retry; a held paste can be put back in the box or saved as a host file whose pointer then travels instead; pictures warn when the active model does not read images; references travel in the submit text while the typed line stays clean | `AttachmentChipAppearsAndCanBeRemoved`, `LongPasteBecomesAChip`, `AttachmentFailureKeepsReasonAndRetry`, `AttachmentPictureWarnsWhenTheModelCannotRead`, `AttachmentReferencesRideWithTheSubmitText`, `KeepingTextCarriesTheTextKind` |
| Launcher recents | `WorkbenchLauncher` | Recent rows keep the filename and add the dimmed directory, as the reference does | Workbench launcher capture |

| Explorer search and sidebar shortcut | Native workbench controls | Clear-search restores the listing without changing the tree; sidebar Files opens the same retained workbench; reveal is hidden for remote workspaces | `WorkbenchClearSearchRestoresTheListing`, `SidebarFilesOpensTheWorkbench` |
| Send/stop/steer/queue/withdraw/restore | Composer plus queue | Draft clears only after acceptance; refusal restores editable state; each runtime owns its queue | Composer/remote-draft/queue regression suites and real streaming case |
| TPS indicator and 44×13 graph | `StudioSparkline` | Four-second arrival window includes text, reasoning and added tool arguments; one-second samples, sixty points, lifetime peak | `GenerationTelemetryTests`; live host stream and running-curve captures |
| TPS detail | `SpeedDetailCard`, `StudioHoverCard` | Pointer and keyboard reveal; current/average/TTFT/output/model time; unsupported timing is “—” | Metric hover tests and four speed pairs |
| Cache/session overview | `SessionOverviewCard` | Canonical ledger, unsplit tokens, cold-start exclusion, coverage and currencies; click pin and context jump | Ledger/masking tests and four full-ledger pairs |
| Context detail | `ContextSummaryCard` | Capacity/categories, largest entries, memory files, MCP/manage and explicit compact confirmation | Hover/inner-memory interaction and context pairs |
| All metric floating cards | `StudioHoverCard` | Pointer bridge, content hover retention, pin toggle, outside/Escape close, switch dismissal, reduced motion | Metric hover suite; 125/150/200% DIP and raster checks |
| Costs/rate/amount visibility/balance | Native metric footer | Supported quote coverage, tariff boundary countdown, session/turn amounts, saved masking, usage destination | Ledger/masking and `WalletOpensTheUsageDestination` |
| Workbench plus and empty state | `WorkbenchLauncher` | Files, new browser, terminal and recent-file entry points | `WorkbenchLauncherAndHiddenFilesUseExistingWorkspaceActions` |
| Explorer header | Native editor/reveal/hidden controls | Existing editor endpoint, acknowledged platform reveal and kernel `hidden=1` listing | Workbench action tests and `NativeFileActionsTests` |
| File menu, tabs and editor status | Native menus and file tabs | Reference/copy/reveal/open, dirty buffers, save conflicts and explicit close choices | Workbench file-menu, buffer/save and close-tab suites |
| Per-runtime workspace | `WorkbenchPresentation` | Files/drafts/folds/caret/Git/browser/terminal selection survive focus; same-runtime rebind retains project state | `RuntimeRebindingKeepsTheWorkspaceDraftAndFileTabs`, real navigation/draft case |
| Git stage/commit/push | Native Git views | Three-state staging readback, reviewed repository/fingerprint, explicit discard/secret acknowledgement/push plan | `WorkbenchGitBindsEveryActionToTheReviewedRepo` and existing real nested-repository tests |
| Browser tabs | `ManualWebPreview`, `StudioSurfaceHost` | New/switch/close/close-other/close-all preserve file drafts; released tab detaches its guest | `WorkbenchBrowserTabsCloseWithoutClosingFiles` |
| Browser address/history/preview | Native WebView2 | HTTP validation, no kernel cookie bridge, explicit generated sandbox document, back/forward/reload | Actual Windows WebView smoke, including retained native history across tabs, at system scaling 150% |
| Terminal | Native terminal surface | Owning engine guards late requests; focus detaches presentation, explicit close ends PTY | Existing terminal tests and workspace lifetime regression |
| Settings destinations | Existing native settings sheet | Search, usage, MCP, controls, worktree and device entry points land at the applicable destination | Palette destination, wallet and settings regression tests |

Visual evidence is linked from `index.html`. Main-page reports retain previously recorded unmatched-node and adjacent chrome/conversation findings. The new metric frame report enforces 0.5 DIP; existing glyph gates retain one pixel of raster tolerance.

Native WebView smoke uses the actual Windows desktop adapter. Scaled headless captures exercise 125%, 150% and 200% without changing the user's Windows display settings.

Live SSH credential negotiation, internet account pairing and macOS/Linux platform adapters were not exercised on this Windows machine. Remote state and refusal paths use canonical HTTP fixtures. No claim about those live external services follows from fixture coverage.

The final integrated run passes 299 baseline tests and 213 native tests with the Windows host required. Strict solution compilation has zero warnings and zero errors. Native WebView2, per-runtime browser cleanup and all four viewport/theme captures are included in validation.json.
