using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Threading;
using Reasonix.Studio.Contracts.Jobs;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Workbench;

/// <summary>The selected job's non-consuming output and follow position in the workbench.</summary>
internal sealed partial class BackgroundJobOutputView : UserControl
{
    /// <summary>The maximum distance from the bottom at which new output is still followed.</summary>
    private const double FollowSlack = 24;

    /// <summary>The job's owning session connection.</summary>
    private readonly StudioPageClient _api;

    /// <summary>Updates the elapsed time independently of the output read.</summary>
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>The most recent acknowledged job details.</summary>
    private BackgroundJobStatus _job;

    /// <summary>The last output reported by the job endpoint.</summary>
    private BackgroundJobOutput? _output;

    /// <summary>The read that belongs to the currently shown tab.</summary>
    private CancellationTokenSource? _reading;

    /// <summary>Whether the workbench currently shows this view.</summary>
    private bool _shown;

    /// <summary>Whether the viewer is keeping the newest output in view.</summary>
    private bool _following = true;

    /// <summary>The current window language.</summary>
    private InterfaceLanguage _language;

    /// <summary>Initializes a new instance of the <see cref="BackgroundJobOutputView"/> class.</summary>
    /// <param name="api">The session's API connection.</param>
    /// <param name="job">The job to open.</param>
    /// <param name="language">The reader's language.</param>
    internal BackgroundJobOutputView(StudioPageClient api, BackgroundJobStatus job, InterfaceLanguage language)
    {
        InitializeComponent();
        _api = api;
        _job = job;
        _clock.Tick += (_, _) => UpdateHeader();
        DetachedFromVisualTree += (_, _) => SetShown(false);
        OutputScroll.ScrollChanged += (_, args) =>
        {
            if (OpeningDock?.Invoke() != true && args.OffsetDelta.Y != 0 && args.ExtentDelta.Y == 0 && args.ViewportDelta.Y == 0)
            {
                _following = OutputScroll.Extent.Height - OutputScroll.Viewport.Height - OutputScroll.Offset.Y <= FollowSlack;
                UpdateFollowing();
            }
        };
        Follow.Click += (_, _) =>
        {
            _following = true;
            UpdateFollowing();
            OutputScroll.ScrollToEnd();
        };
        Stop.Click += async (_, _) =>
        {
            if (!Running || StopRequested is not { } stop)
            {
                return;
            }

            Stop.IsEnabled = false;
            try
            {
                await stop().ConfigureAwait(true);
            }
            finally
            {
                Stop.IsEnabled = true;
            }
        };
        UpdateJob(job, language);
    }

    /// <summary>Gets or sets the current job's explicit stop action.</summary>
    internal Func<Task>? StopRequested
    {
        get; set;
    }

    /// <summary>Gets or sets whether the view's dock width is still changing.</summary>
    internal Func<bool>? OpeningDock
    {
        get; set;
    }

    /// <summary>Gets the stable kernel identifier of this job.</summary>
    internal string JobId => _job.Id;

    /// <summary>Gets the tab caption supplied by the job's caller.</summary>
    internal string TabTitle => string.IsNullOrWhiteSpace(_job.Description) ? _job.Label : _job.Description.Trim();

    /// <summary>Gets the latest known lifecycle state from the job and its output.</summary>
    private string State => _job.Status == "running" && !string.IsNullOrEmpty(_output?.Status) ? _output.Status : _job.Status;

    /// <summary>Gets a value indicating whether fresh output may still appear.</summary>
    private bool Running => State == "running";

    /// <summary>Updates the workbench view from the same status used by the details panel.</summary>
    /// <param name="job">The updated job entry, if the kernel still reports it.</param>
    /// <param name="language">The reader's language.</param>
    internal void UpdateJob(BackgroundJobStatus? job, InterfaceLanguage language)
    {
        if (job is not null)
        {
            _job = job;
        }

        _language = language;
        TitleText.Text = TabTitle;
        ToolTip.SetTip(TitleText, TabTitle);
        var detail = string.IsNullOrWhiteSpace(_job.Detail) ? _job.Label : _job.Detail.Trim();
        Command.IsVisible = !string.IsNullOrWhiteSpace(detail);
        Command.Text = "$ " + detail;
        Stop.Content = MessageCatalog.Get("common.stop", language);
        AutomationProperties.SetName(Follow, MessageCatalog.Get("workbench.job_follow", language));
        ToolTip.SetTip(Follow, MessageCatalog.Get("workbench.job_follow", language));
        UpdateHeader();
    }

    /// <summary>Starts or pauses output reads when the tab is shown or hidden.</summary>
    /// <param name="shown">Whether the reader can see this view.</param>
    internal void SetShown(bool shown)
    {
        if (_shown == shown)
        {
            return;
        }

        _shown = shown;
        if (shown)
        {
            UpdateHeader();
            _ = ReadOutputAsync();
        }
        else
        {
            var reading = _reading;
            _reading = null;
            reading?.Cancel();
            _clock.Stop();
        }
    }

    /// <summary>Follows after layout, once the dock has finished wrapping the output to its final width.</summary>
    internal void FollowLatestOutput() => Dispatcher.UIThread.Post(
        () =>
        {
            if (_shown && _following && OpeningDock?.Invoke() != true)
            {
                OutputScroll.ScrollToEnd();
            }
        },
        DispatcherPriority.Loaded);

    /// <summary>Reads immediately on selection, then polls while the kernel reports running.</summary>
    /// <returns>The lifetime of the selected tab's read loop.</returns>
    private async Task ReadOutputAsync()
    {
        using var reading = new CancellationTokenSource();
        _reading = reading;
        try
        {
            while (!reading.IsCancellationRequested)
            {
                var delay = TimeSpan.FromSeconds(2);
                try
                {
                    var output = await _api.ReadBackgroundJobAsync(JobId, reading.Token).ConfigureAwait(true);
                    if (reading.IsCancellationRequested)
                    {
                        return;
                    }

                    _output = output;
                    ReadError.IsVisible = false;
                    if (Output.Text != output.Text)
                    {
                        Output.Text = output.Text;
                        FollowLatestOutput();
                    }

                    UpdateHeader();
                    if (!Running)
                    {
                        return;
                    }
                }
                catch (KernelRefusal error) when (error.Code == "job.not_running")
                {
                    return;
                }
                catch (Exception error) when (!reading.IsCancellationRequested)
                {
                    ReadError.Text = error is KernelRefusal refusal
                        ? RefusalText.Say(refusal.Reason, refusal.Status, refusal.Detail, _language)
                        : error.Message;
                    ReadError.IsVisible = true;
                    delay = TimeSpan.FromSeconds(4);
                }

                await Task.Delay(delay, reading.Token).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) when (reading.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_reading, reading))
            {
                _reading = null;
            }
        }
    }

    /// <summary>Projects the job lifecycle and elapsed time into its header and footer.</summary>
    private void UpdateHeader()
    {
        var status = State;
        var end = Running ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : _job.EndedAt;
        var elapsed = _job.StartedAt > 0 && end > 0 ? Format.JobElapsed(Math.Max(0, end - _job.StartedAt), _language) : string.Empty;
        var ended = _job.ExitCode is { } code
            ? MessageCatalog.Get("workbench.job_exited", _language, ("code", Format.Count(code, _language)))
            : MessageCatalog.Get("workbench.job_ended", _language);
        JobState.Text = Running ? elapsed : string.IsNullOrEmpty(elapsed) ? ended : ended + " · " + elapsed;
        JobState[!TextBlock.ForegroundProperty] = StudioTypography.Resource(Running ? "NetInkBrush" : status == "failed" ? "ErrInkBrush" : "FaintBrush");
        Mark[!Shape.StrokeProperty] = StudioTypography.Resource(Running ? "NetBrush" : "MutedBrush");
        Stop.IsVisible = Running;
        FollowingBar.IsVisible = Running;
        UpdateFollowing();
        if (Running && _shown)
        {
            _clock.Start();
        }
        else
        {
            _clock.Stop();
        }
    }

    /// <summary>Shows whether the output currently follows the tail.</summary>
    private void UpdateFollowing()
    {
        FollowingText.Text = MessageCatalog.Get(_following ? "workbench.job_following" : "workbench.job_paused", _language);
        Follow.IsVisible = !_following;
    }
}
