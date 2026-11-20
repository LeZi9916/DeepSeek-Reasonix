using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Reasonix.Studio.Kernel.Session;
using Reasonix.Studio.Pages;
using Reasonix.Studio.Shell.Session;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System;
using System.Globalization;
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
    /// The conversation, once a kernel answered.
    /// </summary>
    private SessionEngine? _engine;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class and loads its XAML.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();

        Send.Click += OnSend;
        Composer.KeyDown += OnComposerKey;
        Conversation.Answered += OnAnswered;
        Opened += OnOpened;
        Closed += OnClosed;
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
        _engine!.Changed += OnChanged;

        try
        {
            // The status first, so the chrome names the session before any row
            // arrives; then the transcript the session already holds, so a resumed
            // window opens on its conversation rather than on an empty page.
            var status = await _engine.StatusAsync(_closing.Token).ConfigureAwait(true);
            if (status is not null)
            {
                CrumbTitle.Text = string.IsNullOrEmpty(status.Label) ? "新会话" : status.Label;
                ContextReadout.Text = status.Window > 0
                    ? string.Create(
                        CultureInfo.InvariantCulture,
                        $"{status.Used:N0} / {status.Window:N0}")
                    : status.Used.ToString("N0", CultureInfo.InvariantCulture);
            }

            await _engine.LoadHistoryAsync(_closing.Token).ConfigureAwait(true);
            Conversation.Show(_engine.State);

            // The stream is awaited last and never returns: it is the window's life.
            await _engine.StartAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Closing is the outcome asked for.
        }
        catch (Exception error)
        {
            ShowProblem(error.Message);
        }
    }

    /// <summary>
    /// Stops the kernel when the window closes.
    /// </summary>
    /// <param name="sender">The window.</param>
    /// <param name="e">The event arguments.</param>
    private async void OnClosed(object? sender, EventArgs e)
    {
        await _closing.CancelAsync().ConfigureAwait(true);
        if (_engine is not null)
        {
            _engine.Changed -= OnChanged;
            await _engine.DisposeAsync().ConfigureAwait(true);
            _engine = null;
        }

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
        Dispatcher.UIThread.Post(() => Conversation.Show(state));
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
            ShowProblem(error.Message);
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
    /// Sends on Enter, and leaves Shift+Enter to start a new line.
    /// </summary>
    /// <param name="sender">The composer.</param>
    /// <param name="e">The key event.</param>
    private async void OnComposerKey(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            return;
        }

        e.Handled = true;
        await SubmitAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Sends the composer's line as a turn, once the line has something in it.
    /// </summary>
    /// <returns>A task that completes once the line was accepted or refused.</returns>
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

        try
        {
            Composer.Text = string.Empty;
            await _engine.SubmitAsync(line, _closing.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Closing is the outcome asked for.
        }
        catch (Exception error)
        {
            // The line is put back rather than lost: a refused send holds the
            // person's own words, and dropping them is the worse failure.
            Composer.Text = line;
            ShowProblem(error.Message);
        }
        finally
        {
            _sending.Release();
        }
    }

    /// <summary>
    /// Shows why there is no conversation, where the transcript would be.
    /// </summary>
    /// <param name="problem">The reason, in the person's terms.</param>
    private void ShowProblem(string problem)
    {
        CrumbTitle.Text = "未连接";
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
}
