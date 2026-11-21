---
owner: @esengine
backup: @SivanCola
status: active
reviewed: 2026-11-21
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
| `globalShortcut`, `setGlobalShortcut` | `globalshortcut.js` | `Unsupported` — Avalonia exposes it on Windows only | `TheGlobalShortcutIsUnsupportedRatherThanPretended` |
| `processMemory` | `procmem.js` | `Process.GetCurrentProcess` | `EveryMemberIsImplemented` |
| `openExternal` | `links.js` | `ILauncher.LaunchUriAsync`, http/https only | `EveryMemberIsImplemented` |
| `setUnreadBadge` | `badge.js` | `TrayIcon.ToolTipText` | `EveryMemberIsImplemented` |
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
| page preferences | `prefs.js` | pending `plan_step_16` (the page keeps its own `rx-` keys) | — |
| menu, edit menu | `menu.js`, `editmenu.js` | pending `plan_step_16` | — |
| credential cookie | `credential.js` | `CredentialHandler`, `CredentialKeeper` | `CredentialHandlerTests`, `RealKernelTests` |
| package grants | `packagegrants.js` | pending `plan_step_5` (packaging) | — |
| power, keyboard, input wake | `powerguard.js`, `keyboard.js`, `inputwake.js` | Avalonia handles these; no shell verb | — |
| graphics policy, ozone, app icon | `graphics.js`, `ozone.js`, `appicon.js` | `GraphicsInfo`, `Window.Icon` | — |
| language | `uilang.js` | read from config, as the kernel does | — |
| reload, raise, fullscreen | `reload.js`, `raise.js`, `fullscreen.js` | window verbs above | — |

The two rows marked "see note" are covered by the contract case only: each needs a
real tray or a real log directory on the platform, which a unit test cannot supply
without asserting on the test machine's state rather than on the code. Their
behaviour is checked when the vertical slice runs the real window.

## Manual preview and the agent browser

The workbench manual preview uses the installed system web runtime, with an address bar, back, forward and reload. The URL policy is held by `ManualPreviewTests`. System WebView smoke testing and headless main-window geometry are separate checks.

`browserguard.js`, `browserprotocol.js`, `browserrelay.js`, `browserviews.js`,
`elementpick.js` and `cua.js` draw the agent's pages.

- `browserviews.js`, `browserrelay.js`, `browserprotocol.js`, `elementpick.js` → `plan_step_25`
- `browserguard.js`, `cua.js` → the platform capability layer

`legacy.js` has no counterpart: this shell has no earlier version to migrate from.
