using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Host;

/// <summary>
/// What a window can do and a page cannot, behind one interface so nothing in the
/// shell learns which host it is running on.
/// </summary>
/// <remarks>
/// The surface is the desktop's own, carried over value for value: the page asks
/// for a verb and never for a fact it could have read itself. Everything returns
/// a task because the answer comes from another process, except the predicates a
/// layout decides with before it can await anything.
/// </remarks>
public interface IHostPort
{
    /// <summary>
    /// Gets a value indicating whether this page runs inside a native window, as
    /// opposed to a browser tab where the browser's own keys are the reader's.
    /// </summary>
    bool InShell
    {
        get;
    }

    /// <summary>
    /// Gets a value indicating whether this shell can hand back a picture of its own window.
    /// </summary>
    bool CapturesWindow
    {
        get;
    }

    /// <summary>
    /// Gets a value indicating whether this shell can show a file in the platform's file manager.
    /// </summary>
    bool RevealsFiles
    {
        get;
    }

    /// <summary>
    /// Gets a value indicating whether this shell can open a file with its default program.
    /// </summary>
    bool OpensFiles
    {
        get;
    }

    /// <summary>
    /// Gets a value indicating whether this shell can open the folder it writes its logs to.
    /// </summary>
    bool RevealsLogs
    {
        get;
    }

    /// <summary>
    /// Gets a value indicating whether this shell can enter a folder in the platform's file manager.
    /// </summary>
    bool OpensFolders
    {
        get;
    }

    /// <summary>
    /// Describes the shell without touching the window.
    /// </summary>
    /// <returns>Which shell answered, its platform and whether the page draws the title bar.</returns>
    HostInfo Describe();

    /// <summary>
    /// Minimises the window.
    /// </summary>
    void MinimiseWindow();

    /// <summary>
    /// Maximises the window, or restores it when it is already maximised.
    /// </summary>
    void ToggleMaximiseWindow();

    /// <summary>
    /// Asks the window to close.
    /// </summary>
    void CloseWindow();

    /// <summary>
    /// Reports whether the window is maximised.
    /// </summary>
    /// <returns>True when maximised; false when the shell cannot answer.</returns>
    Task<bool> IsWindowMaximisedAsync();

    /// <summary>
    /// Tells the shell which appearance the page is drawing in, so the window
    /// chrome follows the page rather than the system.
    /// </summary>
    /// <param name="mode">The appearance mode the page resolved.</param>
    /// <param name="dark">Whether the resolved scheme is dark.</param>
    void SyncTheme(string mode, bool dark);

    /// <summary>
    /// Takes a picture of this window as it is drawn now.
    /// </summary>
    /// <returns>
    /// A PNG of the window, or null when the shell cannot or the window has
    /// nothing to show. It names no window: only the asker's own.
    /// </returns>
    Task<byte[]?> CaptureWindowAsync();

    /// <summary>
    /// Reads how the shell draws this launch.
    /// </summary>
    /// <returns>The graphics state, or null where the shell has no say, as in a tab.</returns>
    Task<GraphicsInfo?> GraphicsAsync();

    /// <summary>
    /// Reads the global shortcut the shell currently holds.
    /// </summary>
    /// <returns>The binding and what happened to it.</returns>
    Task<GlobalShortcutInfo> GlobalShortcutAsync();

    /// <summary>
    /// Sets the global shortcut, or clears it with an empty binding.
    /// </summary>
    /// <param name="binding">The shortcut to hold, empty to clear.</param>
    /// <returns>The binding and what happened to it.</returns>
    Task<GlobalShortcutInfo> SetGlobalShortcutAsync(string binding);

    /// <summary>
    /// Reads the shell's own processes for a report about memory.
    /// </summary>
    /// <returns>One entry per process, empty where there is no shell to ask.</returns>
    Task<IReadOnlyList<ProcessMemory>> ProcessMemoryAsync();

    /// <summary>
    /// Opens a URL in whatever the platform uses for it.
    /// </summary>
    /// <param name="url">The address to open.</param>
    /// <returns>A task that completes once the request was handed over.</returns>
    Task OpenExternalAsync(string url);

    /// <summary>
    /// Puts the unread mark on the window's badge.
    /// </summary>
    /// <param name="unread">Whether anything is unread.</param>
    /// <returns>A task that completes once the shell has been told.</returns>
    Task SetUnreadBadgeAsync(bool unread);

    /// <summary>
    /// Reports where a set of dropped files live.
    /// </summary>
    /// <param name="paths">The paths the drop reported.</param>
    /// <returns>
    /// The paths the shell could place; empty where it cannot say, which is what
    /// a browser tab always answers.
    /// </returns>
    IReadOnlyList<string> PathsForFiles(IReadOnlyList<string> paths);

    /// <summary>
    /// Puts text on disk where the person picks.
    /// </summary>
    /// <param name="name">The name to offer in the save dialog.</param>
    /// <param name="content">The text to write.</param>
    /// <returns>
    /// The path written, "" when the person dismissed the dialog, or null when
    /// this shell has no save surface at all.
    /// </returns>
    Task<string?> SaveTextAsync(string name, string content);

    /// <summary>
    /// Puts bytes on disk where the person picks.
    /// </summary>
    /// <param name="name">The name to offer in the save dialog.</param>
    /// <param name="bytes">The bytes to write.</param>
    /// <returns>The path written, "" on dismissal, or null when unsupported.</returns>
    Task<string?> SaveBytesAsync(string name, ReadOnlyMemory<byte> bytes);

    /// <summary>
    /// Shows the platform's text menu for a field or a selection.
    /// </summary>
    /// <param name="request">What the menu may offer and where it goes.</param>
    /// <returns>
    /// What the person chose, "" for a standard native action, or null when this
    /// shell draws no such menu.
    /// </returns>
    Task<string?> ShowTextContextMenuAsync(TextContextMenuRequest request);

    /// <summary>
    /// Asks for a directory.
    /// </summary>
    /// <param name="startIn">Where to open the dialog, or "" for the shell's own default.</param>
    /// <returns>The chosen path, "" on dismissal, or null when there is no picker.</returns>
    Task<string?> PickFolderAsync(string startIn);

    /// <summary>
    /// Shows an entry of the workspace behind a pane in the platform's file manager.
    /// </summary>
    /// <param name="baseAddress">The pane's workspace base, "" for the root itself.</param>
    /// <param name="path">The entry, relative to that root.</param>
    /// <returns>Null once shown, otherwise why it was not.</returns>
    Task<Refusal?> RevealPathAsync(string baseAddress, string path);

    /// <summary>
    /// Opens a file with whatever the platform has registered for it.
    /// </summary>
    /// <param name="baseAddress">The pane's workspace base.</param>
    /// <param name="path">The entry, relative to that root.</param>
    /// <returns>Null once opened, otherwise why it was not.</returns>
    Task<Refusal?> OpenFileAsync(string baseAddress, string path);

    /// <summary>
    /// Shows a project the sidebar lists, which needs no pane.
    /// </summary>
    /// <param name="root">The project's root.</param>
    /// <returns>Null once shown, otherwise why it was not.</returns>
    Task<Refusal?> RevealWorkspaceAsync(string root);

    /// <summary>
    /// Opens the shell's log folder in the platform's file manager.
    /// </summary>
    /// <returns>Null once shown, otherwise why it was not.</returns>
    Task<Refusal?> RevealLogsAsync();

    /// <summary>
    /// Enters a project the sidebar lists.
    /// </summary>
    /// <param name="root">The project's root.</param>
    /// <returns>Null once entered, otherwise why it was not.</returns>
    Task<Refusal?> OpenWorkspaceAsync(string root);
}
