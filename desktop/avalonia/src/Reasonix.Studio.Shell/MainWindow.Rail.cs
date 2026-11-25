using Reasonix.Studio.Contracts.Refusal;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Pages.Rail;
using Reasonix.Studio.Pages.Session;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

// The sidebar's half of the window: which projects the kernel lists, which
// conversation this window has open, and the three writes a row offers.
//
// The tree is read back after every write rather than patched here. A pin made
// in another window, an archive from the CLI and a save from a turn all land in
// one answer, and that answer is the only thing that can say what the column
// holds now.
//
// The type's documentation lives beside its first declaration in MainWindow.axaml.cs.
public sealed partial class MainWindow
{
    /// <summary>
    /// Orders concurrent opens, so only the latest one decides the screen.
    /// </summary>
    private readonly SessionSwitch _switch = new SessionSwitch();

    /// <summary>Only the most recent tree query may update the rail.</summary>
    private long _treeRead;

    /// <summary>
    /// The transcript this window has open, or empty before one is known.
    /// </summary>
    private string _sessionPath = string.Empty;

    /// <summary>
    /// How many notes this window has appended, so no two share a row identity.
    /// </summary>
    private int _notes;

    /// <summary>
    /// Takes on the conversation the kernel says this window is showing.
    /// </summary>
    /// <param name="path">The transcript's path, or empty for a session not yet saved.</param>
    /// <remarks>
    /// A session that has not written a transcript has no path yet, and the rail
    /// draws no row of its own for it: the kernel mints one at the first submit,
    /// and the next tree read carries it.
    /// </remarks>
    private void AdoptSessionPath(string path)
    {
        _sessionPath = path ?? string.Empty;
        var scope = _sessionPath.Length == 0
            ? string.Empty
            : System.Text.Json.JsonSerializer.Serialize(new[] { _engine?.DraftHost ?? string.Empty, _sessionPath });
        Conversation.SetMessageRailScope(scope, _engine);
    }

    /// <summary>
    /// Reads the tree and draws it in the column.
    /// </summary>
    /// <returns>A task that completes once the column is drawn.</returns>
    private async Task LoadTreeAsync()
    {
        if (_closing.IsCancellationRequested || _engine is not { } engine)
        {
            return;
        }

        var ticket = ++_treeRead;
        try
        {
            var query = Projects.WindowFor(RailSearch.Text?.Trim() ?? string.Empty, HideLegacySessions);
            var tree = await engine.TreeAsync(query, _closing.Token).ConfigureAwait(true);
            if (!_closing.IsCancellationRequested && ticket == _treeRead && ReferenceEquals(_engine, engine))
            {
                ShowTree(tree);
                await LoadRemoteSidebarAsync(query, ticket).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            // Closing is the outcome asked for.
        }
        catch (Exception error)
        {
            // A tree that could not be read leaves the column as it was; the
            // transcript is what the window is for, and it keeps working.
            if (!_closing.IsCancellationRequested && ticket == _treeRead && ReferenceEquals(_engine, engine))
            {
                ShowRefusal(error);
            }
        }
    }

    /// <summary>
    /// Draws one tree, marking the conversation this window has open.
    /// </summary>
    /// <param name="tree">The projects the kernel listed.</param>
    private void ShowTree(IReadOnlyList<TreeWorkspace> tree)
    {
        Projects.Language = Language;
        Projects.Show(tree, _sessionPath);
        ObserveUnreadSpan();
        _ = RefreshPeerMarksAsync();
    }

    /// <summary>
    /// Reads the tree again because the kernel said it moved.
    /// </summary>
    private void OnTreeChanged()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(async () => await LoadTreeAsync().ConfigureAwait(true));
    }

    /// <summary>
    /// Opens one conversation the rail listed.
    /// </summary>
    /// <param name="path">The transcript's path.</param>
    /// <returns>A task that completes once the conversation is on screen.</returns>
    /// <remarks>
    /// A refused switch is left where it is: the conversation that was open stays
    /// open, and the refusal says which of "held elsewhere" or "gone" it was.
    /// Concurrent opens are ordered by a ticket, so a slow answer from a row the
    /// person abandoned cannot replace the one they chose.
    /// </remarks>
    private Task OpenSessionAsync(string path)
    {
        var target = Projects.SearchEntries.FirstOrDefault(entry => entry.Target.SessionPath == path && entry.Target.Host.Length == 0).Target;
        return OpenTargetAsync(target ?? new Pages.Rail.SessionOpenTarget(string.Empty, string.Empty, path));
    }

    /// <summary>
    /// Says one thing to the reader where the transcript ends.
    /// </summary>
    /// <param name="note">The sentence to show.</param>
    /// <remarks>
    /// Appended rather than replacing the transcript: what the person was reading
    /// stays where it was, and the note is one more row under it. A later fold
    /// replaces the page and takes the note with it, which is what a note about
    /// one action should do.
    /// </remarks>
    private void ShowNote(string note)
    {
        var state = _engine?.State ?? new SessionState();
        var items = new List<TranscriptItem>(state.Items)
        {
            new NoticeItem
            {
                Id = "note-" + (_notes++).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Level = "info",
                Text = note,
            },
        };

        Conversation.Show(state with
        {
            Items = items,
        });
    }

    /// <summary>
    /// Starts a conversation with nothing in it.
    /// </summary>
    /// <returns>A task that completes once the empty page is on screen.</returns>
    /// <remarks>
    /// The path is cleared first: the kernel mints one at the first submit, and a
    /// stale path here would make the new session look like the old one in the
    /// rail and in the title.
    /// </remarks>
    private Task StartNewSessionAsync() => OpenTargetAsync(new Pages.Rail.SessionOpenTarget(_sidebarHost, _workspaceRoot, string.Empty));

    /// <summary>
    /// Pins or unpins one conversation and reads the tree back.
    /// </summary>
    /// <param name="path">The transcript's path.</param>
    /// <param name="pinned">True to pin, false to unpin.</param>
    /// <returns>A task that completes once the column agrees with the kernel.</returns>
    private async Task PinSessionAsync(string path, bool pinned)
    {
        if (_engine is null)
        {
            return;
        }

        try
        {
            await _engine.PinSessionAsync(path, pinned, _closing.Token).ConfigureAwait(true);
            await LoadTreeAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Closing is the outcome asked for.
        }
        catch (Exception error)
        {
            ShowRefusal(error);
        }
    }

    /// <summary>
    /// Archives or unarchives one conversation and reads the tree back.
    /// </summary>
    /// <param name="path">The transcript's path.</param>
    /// <param name="archived">True to archive, false to bring it back.</param>
    /// <returns>A task that completes once the column agrees with the kernel.</returns>
    private async Task ArchiveSessionAsync(string path, bool archived)
    {
        if (_engine is null)
        {
            return;
        }

        try
        {
            await _engine.ArchiveSessionAsync(path, archived, _closing.Token).ConfigureAwait(true);
            await LoadTreeAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Closing is the outcome asked for.
        }
        catch (Exception error)
        {
            // A conversation a pane is running is refused rather than archived
            // under the turn; the refusal names that, and the row stays as it was.
            ShowRefusal(error);
        }
    }

    /// <summary>
    /// Marks one conversation seen and clears its dot in the column.
    /// </summary>
    /// <param name="path">The transcript's path.</param>
    /// <returns>A task that completes once the column agrees with the kernel.</returns>
    private async Task MarkViewedAsync(string path)
    {
        if (_engine is null)
        {
            return;
        }

        try
        {
            await _engine.MarkSessionViewedAsync(path, _closing.Token).ConfigureAwait(true);
            await LoadTreeAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Closing is the outcome asked for.
        }
        catch (Exception error)
        {
            ShowRefusal(error);
        }
    }

    /// <summary>
    /// Opens the conversation a row asked for.
    /// </summary>
    /// <param name="path">The transcript's path.</param>
    private async void OnProjectOpened(string path)
    {
        await OpenSessionAsync(path).ConfigureAwait(true);
    }

    /// <summary>
    /// Pins or unpins the conversation a row asked for.
    /// </summary>
    /// <param name="path">The transcript's path.</param>
    /// <param name="pinned">True to pin, false to unpin.</param>
    private async void OnProjectPinned(string path, bool pinned)
    {
        await PinSessionAsync(path, pinned).ConfigureAwait(true);
    }

    /// <summary>
    /// Archives or unarchives the conversation a row asked for.
    /// </summary>
    /// <param name="path">The transcript's path.</param>
    /// <param name="archived">True to archive, false to bring it back.</param>
    private async void OnProjectArchived(string path, bool archived)
    {
        await ArchiveSessionAsync(path, archived).ConfigureAwait(true);
    }

    /// <summary>
    /// Marks seen the conversation a row asked for.
    /// </summary>
    /// <param name="path">The transcript's path.</param>
    private async void OnProjectViewed(string path)
    {
        await MarkViewedAsync(path).ConfigureAwait(true);
    }

    /// <summary>
    /// Starts a conversation with nothing in it, as the column's button asks.
    /// </summary>
    private async void OnNewRequested()
    {
        await StartNewSessionAsync().ConfigureAwait(true);
    }
}
