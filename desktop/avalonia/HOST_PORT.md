---
owner: @esengine
backup: @SivanCola
status: active
reviewed: 2026-11-26
---

# Host capability ledger

Every capability the page can ask for, the Electron module that carries it today,
the C# side that carries it here, and the test that holds it. A row without a
test says why, rather than pretending.

## `HostPort` surface

`desktop/frontend-next/src/port/host.ts` declares the interface the page talks to.
Its members are carried over in `desktop/avalonia/src/Reasonix.Studio.Shell/Host/IHostPort.cs`,
with two deliberate changes:

- **Bytes, not DOM types.** `pathsForFiles` takes paths rather than `File` objects:
  Avalonia hands a drop's platform item its path already, so the shell never sees
  the page's file abstraction.
- **No browser-view verbs.** `showBrowserView` and its siblings are absent here.
  They belong to the view host in `plan_step_25`, because they are the agent's
  browser rather than the window's own surface.

| Capability | Electron today | C# here | Test |
| --- | --- | --- | --- |
| `inShell`, `describe` | preload `shell`, `platform`, `titleBar` | `AvaloniaHost.InShell`, `Describe` | `EveryMemberIsImplemented`, `PlatformNameMatchesThePagesSpelling` |
| `minimiseWindow`, `toggleMaximiseWindow`, `closeWindow` | `window.js` | `Window.WindowState`, `Window.Close` | `EveryMemberIsImplemented` |
| `isWindowMaximised` | `window.js` | `Window.WindowState` | `EveryMemberIsImplemented` |
| `syncTheme` | `prefs.js` → `nativeTheme` | `Window.RequestedThemeVariant` | `EveryMemberIsImplemented` |
| `capturesWindow`, `captureWindow` | `capture.js` → `capturePage` | `RenderTargetBitmap` + `PngBitmapEncoderOptions` | `AShellWithoutAWindowAnswersUnsupported` |
| `graphics` | `graphics.js` | the window's `PlatformImpl` type | `EveryMemberIsImplemented` |
| `globalShortcut`, `setGlobalShortcut` | `globalshortcut.js` | macOS, Windows and X11 registrations through the native platform registrars. Unsupported platforms return a typed status. | `GlobalShortcutTests`; Wayland desktop acceptance pending |
| `processMemory` | `procmem.js` | `Process.GetCurrentProcess` | `EveryMemberIsImplemented` |
| `openExternal` | `links.js` | `ILauncher.LaunchUriAsync`, http/https only | `EveryMemberIsImplemented` |
| `setUnreadBadge` | `badge.js` | `DesktopUnreadBadge` updates the macOS Dock label and Windows taskbar overlay. The tray image carries an unread dot where a tray is available, including Linux. | `EveryMemberIsImplemented`; desktop badge acceptance pending |
| `pathsForFiles` | `preload.js` → `webUtils` | the drop's own paths | `EveryMemberIsImplemented` |
| `saveText`, `saveBytes` | dialog IPC | `StorageProvider.SaveFilePickerAsync` | `AShellWithoutAWindowAnswersUnsupported` |
| `showTextContextMenu` | `textcontextmenu.js` | `null`: the control that owns the selection draws its own | `AShellWithoutAWindowAnswersUnsupported` |
| `pickFolder` | dialog IPC | `StorageProvider.OpenFolderPickerAsync` | `AShellWithoutAWindowAnswersUnsupported` |
| `revealPath`, `openFile` | `reveal.js`, `links.js` | `ILauncher`, confined to the workspace base | `IsInsideDoesNotAcceptASiblingWithASharedPrefix` |
| `revealWorkspace`, `openWorkspace` | `folderpanel.js`, `reveal.js` | `ILauncher` | `EveryMemberIsImplemented` |
| `revealLogs` | `shelllog.js` | `ILauncher` on `ShellLog.Directory()` | `EveryMemberIsImplemented` |

**Clipboard is not a host verb.** The page uses `navigator.clipboard`; Electron
touches the system clipboard only in `editmenu.js`/`menu.js`, where it builds the
menu's own cut/copy/paste. Avalonia's text controls carry that themselves.

## The shell's own concerns

| Concern | Electron module | C# here | Test |
| --- | --- | --- | --- |
| single instance | `instance.js` | `SingleInstance` and `InstanceActivation` (Windows event or same-user Unix pipe) | `SecondLaunchIsRefused`, `AnotherLaunchRaisesTheListener` |
| window state memory | `windowstate.js` | `WindowStateStore` | `WindowGeometryRoundTrips`, `AMissingGeometryOpensAtTheDefaultSize` |
| launch logs | `shelllog.js` | `ShellLog` | — (see note) |
| tray icon | `tray.js`, `trayimage.js` | `MainWindow.AttachTray` uses `/tray/prefs` and `/tray/state` for its native menu and close behavior | platform acceptance pending |
| host spawn + lease | `host.js`, `hostclient.js`, `hostprobe.js`, `hostexit.js` | pending `plan_step_16` | — |
| window layout, startup, launcher | `layout.js`, `starting.js`, `launcher.js`, `ground.js` | responsive native window, launch and setup views | `WindowLayoutTests`, `NativeDomainTests` |
| desktop theme | `prefs.js`, scheduled appearance in the page | `WindowStateStore` retains the theme mode; `WorkspacePreferences` retains the local schedule. | `ScheduleReadsLocalTimeIncludingAnOvernightInterval`, `ScheduledThemeShowsItsLocalTimeFields` |
| menu, edit menu | `menu.js`, `editmenu.js` | `ConfigureMacMenu` exposes native Edit and Window commands. Edit actions use the focused Avalonia text controls. | macOS menu acceptance pending |
| credential cookie | `credential.js` | `CredentialHandler`, `CredentialKeeper` | `CredentialHandlerTests`, `RealKernelTests` |
| package grants | `packagegrants.js` | pending `plan_step_5` (packaging) | — |
| prevent sleep while a turn runs | `powerguard.js` | `AppSleepLease` uses Avalonia app-sleep inhibition and the owning host’s `/tray/running` result, also while the window is hidden. The appearance preference is device-local. | `PowerLeaseSeparatesIdleAndUnreachablePanes`; platform inhibition acceptance pending |
| graphics policy, ozone, app icon | `graphics.js`, `ozone.js`, `appicon.js` | `GraphicsInfo`, `Window.Icon` | — |
| language | `uilang.js` | read from config, as the kernel does | — |
| reload, raise, fullscreen | `reload.js`, `raise.js`, `fullscreen.js` | Desktop instance activation raises the native window. F11 toggles Windows and Linux full screen and restores the previous state. | `AnotherLaunchRaisesTheListener`; F11 acceptance pending on Windows and Linux |

The shell contract and headless cases exercise projected state and command routing.
Platform acceptance covers the real tray, menu, Dock and taskbar badge, global
registration, F11, and the operating system’s sleep behavior. The platform badge
uses the application tray on Linux when a tray is available.

## Manual preview and the agent browser

The workbench manual preview uses the installed system web runtime, with an address
bar, back, forward and reload. Retained tabs can be renamed through the menu, F2,
or a double-click. `WorkbenchBrowserTabsCloseWithoutClosingFiles` covers tab
renaming and the retained pages.

`ManualPreviewTests` holds the URL policy. System WebView smoke testing covers
the real native adapter.

The workbench reads existing `GET /browser/tabs` metadata for agent-owned pages.
It shows a read-only label, address and active state and restores the session's
selection. The native surface displays metadata, while the Electron host
modules continue to own the renderer and its input channel.

`legacy.js` has no counterpart: this shell has no earlier version to migrate from.
