using Reasonix.Studio.Contracts.Refusal;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Pages.Rail;
using Reasonix.Studio.Pages.Session;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System;
using System.Collections.Generic;
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
    }

    /// <summary>
    /// Reads the tree and draws it in the column.
    /// </summary>
    /// <returns>A task that completes once the column is drawn.</returns>
    private async Task LoadTreeAsync()
    {
        if (_engine is null)
        {
            return;
        }

        var ticket = ++_treeRead;
        try
        {
            var query = new TreeWindow { Query = RailSearch.Text?.Trim() ?? string.Empty };
            var tree = await _engine.TreeAsync(query, _closing.Token).ConfigureAwait(true);
            if (ticket == _treeRead)
            {
                ShowTree(tree);
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
            ShowRefusal(error);
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
    private async Task OpenSessionAsync(string path)
    {
        if (_engine is null || string.IsNullOrEmpty(path))
        {
            return;
        }

        // Already on screen: a second click is a focus, not a reload, and a
        // reload would drop the reader's scroll position and any half-written line.
        if (RailPresenter.Same(path, _sessionPath))
        {
            return;
        }

        // The same row asked for twice while the first answer is still out is one
        // request; restarting it would abort work the kernel has already done.
        if (_switch.Opening(path))
        {
            return;
        }

        var ticket = _switch.Begin(path);
        try
        {
            if (_launchOwner is not null)
            {
                await ActivateProjectAsync(string.Empty, false, path, () => _switch.IsCurrent(ticket)).ConfigureAwait(true);
                return;
            }

            await _engine.ResumeAsync(path, _closing.Token).ConfigureAwait(true);
            if (!_switch.IsCurrent(ticket))
            {
                // A newer open superseded this one: its answer decides the screen.
                return;
            }

            _sessionPath = path;

            var status = await _engine.StatusAsync(_closing.Token).ConfigureAwait(true);
            if (status is not null)
            {
                CrumbTitle.Text = string.IsNullOrEmpty(status.Label) ? MessageCatalog.Get(MessageKey.Common.NewSession, Language) : status.Label;
                ContextReadout.Text = Reasonix.Studio.Pages.Composer.ContextMeter.Readout(
                    status.Used,
                    status.Window,
                    Language);
                AdoptComposer(status);
            }

            Conversation.Show(_engine.State);
            ShowComposerState(_engine.State);
            await LoadTreeAsync().ConfigureAwait(true);
            await RefreshQueueAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Closing is the outcome asked for.
        }
        catch (Exception error)
        {
            // A superseded open reports nothing: its refusal belongs to a row the
            // person has already left.
            if (!_switch.IsCurrent(ticket))
            {
                return;
            }

            // A history past the read limit cannot open whole. The newest part can
            // be carried into a new transcript; the original file is never touched,
            // so this is the reversible reading of that refusal.
            if (error is IRefusal refusal && SessionBoundaries.Of(refusal) == SessionBoundary.TooLarge)
            {
                await ContinueFromTailAsync(path).ConfigureAwait(true);
                return;
            }

            ShowRefusal(error);
        }
        finally
        {
            _switch.End(ticket);
        }
    }

    /// <summary>
    /// Carries the newest part of an oversized conversation into a new transcript
    /// and opens that copy.
    /// </summary>
    /// <param name="path">The transcript whose history is past the read limit.</param>
    /// <returns>A task that completes once the copy is open or the failure is shown.</returns>
    /// <remarks>
    /// The original stays exactly as it is: the copy is a new file, and the note
    /// says how many messages stayed behind so the person can tell what they are
    /// reading.
    /// </remarks>
    private async Task ContinueFromTailAsync(string path)
    {
        if (_engine is null)
        {
            return;
        }

        try
        {
            var tail = await _engine.TailAsync(path, _closing.Token).ConfigureAwait(true);
            if (string.IsNullOrEmpty(tail.Path))
            {
                return;
            }

            await OpenSessionAsync(tail.Path).ConfigureAwait(true);
            ShowNote(MessageCatalog.Get(
                MessageKey.Session.SavedTheMostRecentPart,
                Language,
                ("n", Format.Count(tail.Dropped, Language))));
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
    private async Task StartNewSessionAsync()
    {
        if (_engine is null)
        {
            return;
        }

        var ticket = _switch.Begin(string.Empty);
        try
        {
            if (NewTaskPreset is not null)
            {
                await ActivateProjectAsync(_workspaceRoot, false, null, () => _switch.IsCurrent(ticket)).ConfigureAwait(true);
                return;
            }

            await _engine.NewSessionAsync(_closing.Token).ConfigureAwait(true);
            if (!_switch.IsCurrent(ticket))
            {
                return;
            }

            _sessionPath = string.Empty;
            CrumbTitle.Text = MessageCatalog.Get(MessageKey.Common.NewSession, Language);
            ContextReadout.Text = "—";
            Composer.Text = string.Empty;
            Conversation.Show(_engine.State);
            ShowComposerState(_engine.State);
            await LoadTreeAsync().ConfigureAwait(true);
            await RefreshQueueAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Closing is the outcome asked for.
        }
        catch (Exception error)
        {
            if (_switch.IsCurrent(ticket))
            {
                ShowRefusal(error);
            }
        }
        finally
        {
            _switch.End(ticket);
        }
    }

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
