using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Reasonix.Studio.Contracts.Queue;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.Composer;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Host;
using Reasonix.Studio.State;
using System;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

// The composer's half of the window: the draft it holds, the key that sends it,
// what it will do with the line, and the queue waiting behind the turn.
//
// Kept beside the window rather than in the page because all of it is about the
// line being written, not about the transcript being read: the draft belongs to
// one conversation, the shortcut belongs to this machine, and the queue belongs
// to the kernel.
//
// The type's documentation lives beside its first declaration in MainWindow.axaml.cs.
public sealed partial class MainWindow
{
    /// <summary>
    /// Where unsent lines are kept between launches.
    /// </summary>
    private readonly IDraftStorage _drafts = FileDrafts.Default();

    /// <summary>
    /// The key that sends, as this machine last chose it.
    /// </summary>
    private SendShortcut _shortcut = SendShortcutStore.Load();

    /// <summary>
    /// The draft key of the conversation on screen, or empty before one is known.
    /// </summary>
    private string _draftKey = string.Empty;

    /// <summary>
    /// The context window the kernel last reported, which the meter draws against.
    /// </summary>
    private long _window;

    /// <summary>
    /// The queue as the kernel last held it, or null before the first read.
    /// </summary>
    private QueueSnapshot? _queue;

    /// <summary>Gets or sets a value indicating whether messages during a run are queued.</summary>
    internal bool QueueDuringRun
    {
        get; set;
    }

    /// <summary>
    /// Gets the language this window draws its own labels in.
    /// </summary>
    private InterfaceLanguage Language => Conversation.Language;

    /// <summary>
    /// Takes on the conversation's draft and meter once the status is known.
    /// </summary>
    /// <param name="status">What the kernel said about the open session.</param>
    internal void AdoptComposer(SessionStatus status)
    {
        BindWorkspace(status.WorkspaceRoot);
        _window = status.Window;
        SetModelLabel(status);
        if (!string.IsNullOrEmpty(status.WorkspaceRoot))
        {
            ProjectName.Text = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(status.WorkspaceRoot));
            ToolTip.SetTip(ProjectName, status.WorkspaceRoot);
        }

        if (_engine is null)
        {
            return;
        }

        _draftKey = DraftStore.KeyFor(_engine.DraftHost, status.WorkspaceRoot, status.SessionPath);
        Composer.Text = DraftStore.Read(_drafts, _draftKey);
        Composer.CaretIndex = Composer.Text?.Length ?? 0;
        DismissRefinement();
        _ = CloseSideQuestionAsync();
        _ = LoadComposeTelemetryAsync();
        _ = LoadGitAsync();
    }

    /// <summary>Shows the model the kernel reported.</summary>
    /// <param name="status">The current session status.</param>
    private void SetModelLabel(SessionStatus status)
    {
        ShowComposeStatus(status);
        ToolTip.SetTip(ModelButton, string.IsNullOrEmpty(status.ModelRef) ? MessageCatalog.Get(MessageKey.Composer.ChooseModel, Language) : status.ModelRef);
    }

    /// <summary>
    /// Keeps the line being written, so a switch or a relaunch finds it again.
    /// </summary>
    private void SaveDraft()
    {
        DraftStore.Write(_drafts, _draftKey, Composer.Text ?? string.Empty);
    }

    /// <summary>
    /// Redraws the status line from the state the conversation now holds.
    /// </summary>
    /// <param name="state">The state the fold produced.</param>
    private void ShowComposerState(SessionState state)
    {
        ShowSidebarState(state);
        var running = state.Running;

        // While a turn runs, a line is guidance unless the person chose to queue.
        // The preference is steer, which is the reading the reference frontend
        // starts from too.
        var intent = SendKey.IntentFor(running, QueueDuringRun ? SendKey.Intent.FollowUp : SendKey.Intent.Steer);
        var shortcutHint = SendKey.Hint(_shortcut, running, Language, intent);
        ComposerHint.Text = running ? MessageCatalog.Get(state.Doing, Language) : shortcutHint;
        DrawComposeMeters(state);
        ComposerHint.IsVisible = running;
        Composer.PlaceholderText = MessageCatalog.Get(
            running ? MessageKey.Composer.YouCanKeepTypingWhile : MessageKey.Composer.DescribeATaskQuestionOr,
            Language);
        var runLabel = QueueDuringRun ? MessageKey.Composer.Queue : MessageKey.Common.Steer;
        Label(Send, running ? runLabel : MessageKey.Common.Send);
        SendIntent.Text = MessageCatalog.Get(runLabel, Language);
        SendIntent.IsVisible = running;
        Send.Width = running ? double.NaN : 32;
        Send.Padding = running ? new Thickness(10, 0, 12, 0) : default;
        ToolTip.SetTip(Send, shortcutHint);
        Send.IsEnabled = !string.IsNullOrWhiteSpace(Composer.Text);
        Stop.IsVisible = running;
        if (!running)
        {
            Stop.IsEnabled = true;
        }

        Label(Stop, Stop.IsEnabled ? MessageKey.Common.Stop : MessageKey.Common.Stopping);
        if (AnalysisPanel.IsVisible && _analysisWasRunning && !running)
        {
            _ = LoadAnalysisAsync();
        }

        _analysisWasRunning = running;

        DrawQueue(state);
    }

    /// <summary>
    /// Reads the queue again, because the kernel said it moved.
    /// </summary>
    /// <returns>A task that completes once the strip is redrawn.</returns>
    private async Task RefreshQueueAsync()
    {
        if (_engine is null)
        {
            return;
        }

        try
        {
            _queue = await _engine.QueueAsync(_closing.Token).ConfigureAwait(true);
            DrawQueue(Conversation.State);
        }
        catch (OperationCanceledException)
        {
            // Closing is the outcome asked for.
        }
        catch (Exception)
        {
            // A queue that could not be read draws as nothing; the next move
            // brings another frame and another read.
            _queue = null;
            DrawQueue(Conversation.State);
        }
    }

    /// <summary>
    /// Draws the lines the kernel holds but has not dispatched.
    /// </summary>
    /// <param name="state">The state the conversation now holds.</param>
    private void DrawQueue(SessionState state)
    {
        QueueStrip.Children.Clear();

        var rows = QueuePresenter.Rows(_queue, state.Running, Language);
        QueueScroll.IsVisible = rows.Count > 0;
        foreach (var row in rows)
        {
            QueueStrip.Children.Add(Row(row));
        }

        // A queue the kernel paused keeps its lines and dispatches nothing. The
        // release is offered once, above the list, because every line waits on
        // the same answer.
        if (rows.Count > 0 && QueuePresenter.OffersRelease(_queue))
        {
            QueueStrip.Children.Insert(0, Action(MessageKey.Composer.Release, ReleaseQueueAsync));
        }
    }

    /// <summary>
    /// Builds one queue line.
    /// </summary>
    /// <param name="row">The line to draw.</param>
    /// <returns>The control, with the actions the kernel offers for it.</returns>
    private Control Row(QueueRow row)
    {
        var text = new StackPanel
        {
            Spacing = 1,
            Orientation = Orientation.Vertical,
        };
        text.Children.Add(new TextBlock
        {
            Text = row.Label,
            [!TextBlock.FontSizeProperty] = StudioTypography.Size(11),
            Opacity = 0.7,
        });
        text.Children.Add(new TextBlock
        {
            Text = row.Preview,
            [!TextBlock.FontSizeProperty] = StudioTypography.Size(12),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        if (row.Why is { Length: > 0 } why)
        {
            text.Children.Add(new TextBlock
            {
                Text = why,
                [!TextBlock.FontSizeProperty] = StudioTypography.Size(11),
                Opacity = 0.55,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (row.CanSteer)
        {
            actions.Children.Add(Action(MessageKey.Composer.MakeItASteer, () => ToSteerAsync(row.Id)));
        }

        if (row.CanSendNow)
        {
            actions.Children.Add(Action(MessageKey.Composer.SendNow, () => SendNowAsync(row.Id)));
        }

        if (row.CanRetry)
        {
            actions.Children.Add(Action(MessageKey.Common.Retry, () => RetryAsync(row.Id)));
        }

        if (row.CanCancel)
        {
            actions.Children.Add(Action(MessageKey.Composer.Withdraw, () => WithdrawAsync(row.Id)));
        }

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(16, 2, 16, 2),
        };
        grid.Children.Add(text);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);
        return grid;
    }

    /// <summary>
    /// Builds one action button for a queue line.
    /// </summary>
    /// <param name="key">The message key naming the action.</param>
    /// <param name="run">What pressing it asks the kernel.</param>
    /// <returns>The button.</returns>
    private Control Action(string key, Func<Task> run)
    {
        var button = new Button
        {
            Content = MessageCatalog.Get(key, Language),
            [!TextBlock.FontSizeProperty] = StudioTypography.Size(11),
            Padding = new Thickness(6, 2, 6, 2),
            Background = Brushes.Transparent,
        };
        button.Click += async (_, _) => await Ask(run).ConfigureAwait(true);
        return button;
    }

    /// <summary>
    /// Runs one queue action, keeping a refusal where the reader can see it.
    /// </summary>
    /// <param name="run">The action.</param>
    /// <returns>A task that completes once the action settled.</returns>
    private async Task Ask(Func<Task> run)
    {
        try
        {
            await run().ConfigureAwait(true);
            await RefreshQueueAsync().ConfigureAwait(true);
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
    /// Takes one line back into the composer.
    /// </summary>
    /// <param name="id">The entry to take back.</param>
    /// <returns>A task that completes once the text is back in the line.</returns>
    private async Task WithdrawAsync(string id)
    {
        if (_engine is null)
        {
            return;
        }

        var text = await _engine.WithdrawAsync(id, _closing.Token).ConfigureAwait(true);
        Composer.Text = text;
        Composer.CaretIndex = Composer.Text?.Length ?? 0;
        SaveDraft();
    }

    /// <summary>
    /// Asks the kernel to retry a blocked line.
    /// </summary>
    /// <param name="id">The entry to retry.</param>
    /// <returns>A task that completes once the kernel accepted.</returns>
    private async Task RetryAsync(string id)
    {
        if (_engine is null)
        {
            return;
        }

        await _engine.RetryQueuedAsync(id, _closing.Token).ConfigureAwait(true);
    }

    /// <summary>
    /// Moves a queued line to the head and stops the turn holding the session.
    /// </summary>
    /// <param name="id">The entry to send now.</param>
    /// <returns>A task that completes once the kernel has both accepted.</returns>
    private async Task SendNowAsync(string id)
    {
        if (_engine is null)
        {
            return;
        }

        await _engine.SendNowAsync(id, _closing.Token).ConfigureAwait(true);
    }

    /// <summary>
    /// Takes a queued follow-up back and says it again as guidance.
    /// </summary>
    /// <param name="id">The entry to turn into guidance.</param>
    /// <returns>A task that completes once the line is guidance or queued again.</returns>
    private async Task ToSteerAsync(string id)
    {
        if (_engine is null)
        {
            return;
        }

        var text = await _engine.WithdrawAsync(id, _closing.Token).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        await _engine.SendAsync(text, steer: true, _closing.Token).ConfigureAwait(true);
    }

    /// <summary>
    /// Holds the queue, or lets it dispatch again.
    /// </summary>
    /// <param name="paused">True to hold dispatch, false to release it.</param>
    /// <returns>A task that completes once the kernel recorded the state.</returns>
    private async Task SetQueuePausedAsync(bool paused)
    {
        if (_engine is null)
        {
            return;
        }

        await _engine.SetQueuePausedAsync(paused, _closing.Token).ConfigureAwait(true);
    }

    /// <summary>
    /// Lets a paused queue dispatch again.
    /// </summary>
    /// <returns>A task that completes once the kernel recorded the release.</returns>
    private Task ReleaseQueueAsync()
    {
        return SetQueuePausedAsync(paused: false);
    }

    /// <summary>
    /// Reads the queue once the conversation is open, so a waiting line is on
    /// screen before anything moves it.
    /// </summary>
    /// <returns>A task that completes once the strip is drawn.</returns>
    private Task LoadQueueAsync()
    {
        return RefreshQueueAsync();
    }

    /// <summary>
    /// Posts a lane-null redraw of the queue.
    /// </summary>
    private void PostQueueRefresh()
    {
        Dispatcher.UIThread.Post(async () => await RefreshQueueAsync().ConfigureAwait(true));
    }
}
