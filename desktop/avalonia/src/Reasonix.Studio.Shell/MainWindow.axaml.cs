using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Contracts.Refusal;
using Reasonix.Studio.Kernel.Session;
using Reasonix.Studio.Pages;
using Reasonix.Studio.Pages.Composer;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Session;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

/// <summary>
/// The top-level window Reasonix Studio opens on launch.
/// </summary>
/// <remarks>
/// The window owns one conversation for its whole life: it starts the kernel when
/// the window opens, folds what the kernel says into the page, and stops the kernel
/// when it closes. A launch that cannot find or start a kernel still opens and says
/// so in the transcript — a window that fails to appear explains nothing.
/// </remarks>
public sealed partial class MainWindow : Window
{
    /// <summary>
    /// Cancels the launch and the stream when the window closes.
    /// </summary>
    private readonly CancellationTokenSource _closing = new CancellationTokenSource();

    /// <summary>
    /// Serialises submits, so pressing send twice does not interleave two lines.
    /// </summary>
    private readonly SemaphoreSlim _sending = new SemaphoreSlim(1, 1);

    /// <summary>
    /// Whether opening this window starts a kernel beside it.
    /// </summary>
    private readonly bool _connectOnOpen;

    /// <summary>
    /// The conversation, once a kernel answered.
    /// </summary>
    private SessionEngine? _engine;

    /// <summary>Owns the launch after the window switches to a child workspace runtime.</summary>
    private SessionEngine? _launchOwner;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class and loads its XAML.
    /// </summary>
    public MainWindow()
        : this(connectOnOpen: true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class, optionally
    /// without a kernel.
    /// </summary>
    /// <param name="connectOnOpen">
    /// Whether opening the window starts a kernel. False draws the window on the
    /// state its caller hands it, which is what the geometry gate needs: a window
    /// that started a kernel would replace the page under measurement with
    /// whatever that kernel answered, including the notice that there is none.
    /// </param>
    public MainWindow(bool connectOnOpen)
        : this(connectOnOpen, null, LanguageResolver.FromMachine())
    {
    }

    /// <summary>Initializes a new instance of the <see cref="MainWindow"/> class with an existing transport.</summary>
    /// <param name="connectOnOpen">Whether to launch the desktop host on opening.</param>
    /// <param name="engine">Session already connected to a host, if any.</param>
    /// <param name="language">Window language on first paint.</param>
    /// <param name="systemAllowsMotion">Optional platform preference provider.</param>
    internal MainWindow(bool connectOnOpen, SessionEngine? engine, InterfaceLanguage language, Func<CancellationToken, Task<bool>>? systemAllowsMotion = null)
    {
        _systemAllowsMotion = systemAllowsMotion ?? SystemAllowsMotionAsync;
        InitializeComponent();
        _engine = engine;

        _connectOnOpen = connectOnOpen;

        Conversation.Language = language;
        Projects.Language = Language;
        CrumbTitle.Text = MessageCatalog.Get(MessageKey.Common.NewSession, Language);
        InitializeChrome();
        InitializeIntake();
        InitializePreferences();
        Conversation.SubmitAnswers = (id, answers) => TryAnswerAsync(() => _engine!.AnswerAsync(id, answers, _closing.Token));
        Conversation.EngageQuestion = id => TryAnswerAsync(() => _engine!.Pages.EngageAsync(id, _closing.Token));
        Label(DismissError, MessageKey.Common.Close);
        DismissError.Click += (_, _) => ErrorBar.IsVisible = false;
        ShowComposerState(Conversation.State);
        Send.Click += OnSend;
        Stop.Click += OnStop;
        Composer.AddHandler(KeyDownEvent, OnComposerKey, RoutingStrategies.Tunnel);
        Composer.TextChanged += OnComposerTextChanged;
        Conversation.Answered += OnAnswered;
        Conversation.QuoteRequested += passage =>
        {
            var quote = string.Join(Environment.NewLine, passage.Split('\n').Select(line => "> " + line));
            Composer.SelectedText = quote + Environment.NewLine + Environment.NewLine;
            Composer.Focus();
        };
        Projects.Opened += OnProjectOpened;
        Projects.Pinned += OnProjectPinned;
        Projects.Archived += OnProjectArchived;
        Projects.MarkedViewed += OnProjectViewed;
        NewSession.Click += (_, _) => OnNewRequested();
        Closed += OnClosed;

        // The status line says what the next press does before anything is
        // typed, so it is filled in from the shortcut rather than left blank.
        ComposerHint.Text = SendKey.Hint(_shortcut, running: false, Language);

        if (connectOnOpen)
        {
            Opened += OnOpened;
        }
    }

    /// <summary>
    /// Shows a conversation state without a kernel behind it.
    /// </summary>
    /// <param name="state">The state to draw.</param>
    /// <remarks>
    /// The geometry gate renders this window with no kernel and hands it the
    /// conversation that was captured, because a window that started a kernel
    /// would swap the page under measurement for whatever that kernel answered.
    /// </remarks>
    public void ShowState(SessionState state)
    {
        RefreshGitAfterTurn(state.Running);
        UpdateMemorySuggestions(state.Running);
        Backdrop.SetBusy(state.Running);
        Conversation.Show(state);
        ShowComposerState(state);
    }

    /// <summary>Keeps a refused question editable and presents the kernel's reason.</summary>
    /// <param name="action">The kernel write.</param>
    /// <returns>Whether the kernel accepted the interaction.</returns>
    private async Task<bool> TryAnswerAsync(Func<Task> action)
    {
        if (_engine is null || _closing.IsCancellationRequested)
        {
            return false;
        }

        try
        {
            await action().ConfigureAwait(true);
            return true;
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception error)
        {
            ShowRefusal(error);
            return false;
        }
    }

    /// <summary>Cancels the active turn while leaving any queued follow-ups alone.</summary>
    /// <param name="sender">The stop button.</param>
    /// <param name="e">The click.</param>
    private async void OnStop(object? sender, RoutedEventArgs e)
    {
        if (_engine is null || !Conversation.State.Running || !Stop.IsEnabled)
        {
            return;
        }

        Stop.IsEnabled = false;
        Label(Stop, MessageKey.Common.Stopping);
        try
        {
            await _engine.CancelTurnAsync(_closing.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            Stop.IsEnabled = true;
            Label(Stop, MessageKey.Common.Stop);
            ShowRefusal(error);
        }
    }

    /// <summary>
    /// Starts the conversation once the window is on screen.
    /// </summary>
    /// <param name="sender">The window.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnOpened(object? sender, EventArgs e)
    {
        var launch = await new SessionLauncher().LaunchAsync(_closing.Token).ConfigureAwait(true);
        if (!launch.Started)
        {
            ShowProblem(launch.Problem ?? "这个会话没有启动。");
            return;
        }

        _engine = launch.Engine;
        _ = RefreshTrayAsync();
        _engine!.Changed += OnChanged;
        _engine.QueueChanged += OnQueueChanged;
        _engine.TreeChanged += OnTreeChanged;

        // A card whose middle the page let go of asks for the whole output
        // through this; the engine is what knows how to walk the kernel's pages.
        Conversation.ReadFullOutput = callId => _engine.ReadFullOutputAsync(callId, _closing.Token);

        try
        {
            // The status first, so the chrome names the session before any row
            // arrives; then the transcript the session already holds, so a resumed
            // window opens on its conversation rather than on an empty page.
            var status = await _engine.StatusAsync(_closing.Token).ConfigureAwait(true);
            if (status is not null)
            {
                CrumbTitle.Text = string.IsNullOrEmpty(status.Label) ? MessageCatalog.Get(MessageKey.Common.NewSession, Language) : status.Label;
                ContextReadout.Text = ContextMeter.Readout(status.Used, status.Window, Conversation.Language);

                // The draft and the meter both need what only the status carries:
                // the transcript the draft belongs to, and the window to draw
                // usage against.
                AdoptComposer(status);
                AdoptSessionPath(status.SessionPath);
            }

            await _engine.LoadHistoryAsync(_closing.Token).ConfigureAwait(true);
            Conversation.Show(_engine.State);
            ShowComposerState(_engine.State);

            // A line another window already queued is on screen before anything
            // moves; the stream then says when to read it again. The tree is the
            // same kind of read: the column shows what the kernel holds now.
            await LoadQueueAsync().ConfigureAwait(true);
            await LoadTreeAsync().ConfigureAwait(true);

            // The stream is awaited last and never returns: it is the window's life.
            UpdateMemorySuggestions(_engine.State.Running);
            _ = LoadStartupPreferencesAsync();
            await _engine.StartAsync().ConfigureAwait(true);
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
    /// Stops the kernel when the window closes.
    /// </summary>
    /// <param name="sender">The window.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnClosed(object? sender, EventArgs e)
    {
        // The line on screen is the person's last unsent words; it is written
        // before the window tears anything down.
        SaveDraft();

        ResetGit();
        await _closing.CancelAsync().ConfigureAwait(true);
        if (_engine is not null)
        {
            _engine.Changed -= OnChanged;
            _engine.QueueChanged -= OnQueueChanged;
            _engine.TreeChanged -= OnTreeChanged;
            await _engine.DisposeAsync().ConfigureAwait(true);
            if (_launchOwner is not null && !ReferenceEquals(_launchOwner, _engine))
            {
                await _launchOwner.DisposeAsync().ConfigureAwait(true);
            }

            _engine = null;
        }

        await _sending.WaitAsync().ConfigureAwait(true);
        _sending.Dispose();
        _closing.Dispose();
    }

    /// <summary>
    /// Draws a state the conversation produced.
    /// </summary>
    /// <param name="state">The state to draw.</param>
    /// <remarks>
    /// The fold happens on the stream's own loop, so the draw is marshalled: a view
    /// touches its controls only where it may, and that decision belongs here rather
    /// than in the engine.
    /// </remarks>
    private void OnChanged(SessionState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(_engine?.State, state))
            {
                ShowState(state);
            }
        });
    }

    /// <summary>
    /// Reads the queue again because the kernel said it moved.
    /// </summary>
    /// <remarks>
    /// The frame carries no content, so the answer is the kernel's own snapshot
    /// read back: that is also what puts another window's lines, and the CLI's,
    /// in front of this one.
    /// </remarks>
    private void OnQueueChanged()
    {
        PostQueueRefresh();
    }

    /// <summary>
    /// Sends the verdict a person pressed on an approval card.
    /// </summary>
    /// <param name="verdict">The id the kernel named and the scope the person chose.</param>
    private async void OnAnswered(ApprovalVerdict verdict)
    {
        if (_engine is null)
        {
            return;
        }

        try
        {
            await _engine
                .ApproveAsync(verdict.Id, verdict.Allow, verdict.Session, verdict.Persist, _closing.Token)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Closing is the outcome asked for.
        }
        catch (Exception error)
        {
            // A verdict the kernel would not take leaves the card open, which is
            // the truthful state: nothing was decided.
            ShowRefusal(error);
        }
    }

    /// <summary>
    /// Sends what the composer holds.
    /// </summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnSend(object? sender, RoutedEventArgs e)
    {
        await SubmitAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Sends on the key this machine chose, and leaves the other Enter to start a
    /// new line.
    /// </summary>
    /// <param name="sender">The composer.</param>
    /// <param name="e">The key event.</param>
    private async void OnComposerKey(object? sender, KeyEventArgs e)
    {
        if (Composer.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault()?.PreeditText is { Length: > 0 })
        {
            return;
        }

        if (HandleSuggestionKey(e))
        {
            return;
        }

        var enter = e.Key == Key.Enter;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);

        if (!SendKey.SendsOnKey(enter, shift, control, _shortcut))
        {
            return;
        }

        e.Handled = true;
        await SubmitAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Keeps the line being written, so a switch or a relaunch finds it again.
    /// </summary>
    /// <param name="sender">The composer.</param>
    /// <param name="e">The text change.</param>
    private void OnComposerTextChanged(object? sender, TextChangedEventArgs e)
    {
        SaveDraft();
    }

    /// <summary>
    /// Sends the composer's line, as guidance or as a queued turn.
    /// </summary>
    /// <returns>A task that completes once the line was accepted or refused.</returns>
    /// <remarks>
    /// While a turn runs the line is guidance: it slips into that turn at its
    /// next tool boundary instead of starting a second one. Nothing running means
    /// a follow-up, which the kernel dispatches as the next turn.
    /// </remarks>
    private async Task SubmitAsync()
    {
        if (_engine is null)
        {
            return;
        }

        var line = Composer.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        // One send at a time: two submits in flight would interleave, and the second
        // would be a follow-up the person never meant to queue.
        if (!await _sending.WaitAsync(0).ConfigureAwait(true))
        {
            return;
        }

        var draft = _draftKey;
        var steer = Conversation.State.Running && !QueueDuringRun;
        Composer.IsReadOnly = true;
        try
        {
            await _engine.SendAsync(line, steer, _closing.Token).ConfigureAwait(true);
            if (draft == _draftKey && Composer.Text == line)
            {
                Composer.Text = string.Empty;
            }

            if (DraftStore.Read(_drafts, draft) == line)
            {
                DraftStore.Write(_drafts, draft, string.Empty);
            }

            if (draft == _draftKey)
            {
                await RefreshQueueAsync().ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (draft == _draftKey)
            {
                ShowRefusal(error);
            }
        }
        finally
        {
            Composer.IsReadOnly = false;
            _sending.Release();
        }
    }

    /// <summary>
    /// Shows why there is no conversation, where the transcript would be.
    /// </summary>
    /// <param name="problem">The reason, in the person's terms.</param>
    private void ShowProblem(string problem)
    {
        CrumbTitle.Text = MessageCatalog.Get(MessageKey.Common.NotConnected, Language);
        Conversation.Show(new SessionState
        {
            Items = new TranscriptItem[]
            {
                new NoticeItem
                {
                    Id = "problem",
                    Level = "warn",
                    Text = problem,
                },
            },
        });
    }

    /// <summary>
    /// Shows why an action was refused, in the identity that refusal carried.
    /// </summary>
    /// <param name="error">The failure, which may be a coded refusal.</param>
    /// <remarks>
    /// A refusal crosses with a status and a dotted code; <see cref="RefusalText"/>
    /// turns that into the sentence this window reads. Anything else — a bad
    /// argument, a socket that went away — has no code to look up, so its own
    /// message is what there is.
    /// </remarks>
    private void ShowRefusal(Exception error)
    {
        ErrorBar.IsVisible = true;
        ErrorText.Text = DescribeError(error);
    }

    /// <summary>Translates a host refusal for any window surface.</summary>
    /// <param name="error">The request failure.</param>
    /// <returns>The reason to present.</returns>
    private string DescribeError(Exception error)
    {
        return error is IRefusal refusal
            ? RefusalText.Say(refusal.Reason, refusal.Status, refusal.Detail, Conversation.Language)
            : error.Message;
    }
}
