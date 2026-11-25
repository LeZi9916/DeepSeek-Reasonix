using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Reasonix.Studio.Contracts.Jobs;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Composer;

/// <summary>The background jobs reading and detail panel on the composer meter rail.</summary>
internal sealed partial class BackgroundJobChip : UserControl
{
    /// <summary>Completed failures already acknowledged in this conversation.</summary>
    private readonly HashSet<string> _seenFailures = new(StringComparer.Ordinal);

    /// <summary>The ended jobs dismissed by the reader in this conversation.</summary>
    private readonly HashSet<string> _cleared = new(StringComparer.Ordinal);

    /// <summary>Times the confirmation window after the reader first asks to stop.</summary>
    private readonly DispatcherTimer _confirmTimeout = new() { Interval = TimeSpan.FromSeconds(3) };

    /// <summary>Updates elapsed time while the job card is visible.</summary>
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>Each current running row and its elapsed-time label.</summary>
    private readonly List<(BackgroundJobStatus Job, TextBlock Label)> _elapsed = [];

    /// <summary>The current stop buttons and their confirmation controls.</summary>
    private readonly Dictionary<string, (Button Request, StackPanel Confirmation, string[] Jobs)> _controls = new(StringComparer.Ordinal);

    /// <summary>Job identifiers with an in-flight stop request.</summary>
    private readonly HashSet<string> _stopping = new(StringComparer.Ordinal);

    /// <summary>The currently entering confirmation, if motion is enabled.</summary>
    private CancellationTokenSource? _confirmationMotion;

    /// <summary>The job or group awaiting its second, explicit confirmation.</summary>
    private string? _armed;

    /// <summary>The last jobs read from the kernel, in its order.</summary>
    private BackgroundJobStatus[] _jobs = [];

    /// <summary>The session and language that own the current reading.</summary>
    private (string Path, string Workspace, InterfaceLanguage Language)? _source;

    /// <summary>Initializes a new instance of the <see cref="BackgroundJobChip"/> class.</summary>
    public BackgroundJobChip()
    {
        InitializeComponent();
        _confirmTimeout.Tick += (_, _) => SetArmed(null);
        _clock.Tick += (_, _) =>
        {
            if (Details.IsOpen && _source is { } source)
            {
                foreach (var (job, label) in _elapsed)
                {
                    label.Text = StatusOf(job, source.Language);
                }
            }
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _clock.Stop();
            SetArmed(null);
        };
        Details.Pinned += () =>
        {
            foreach (var job in _jobs.Where(job => job.Status == "failed"))
            {
                _seenFailures.Add(job.Id);
            }

            UpdateTrigger(_source?.Language ?? InterfaceLanguage.Zh);
        };
    }

    /// <summary>Raised when the reader opens a job's output in the workbench.</summary>
    internal event Action<BackgroundJobStatus>? OutputRequested;

    /// <summary>Publishes the visible jobs to the transcript's background tool cards.</summary>
    internal event Action<IReadOnlyList<BackgroundJobStatus>>? JobsChanged;

    /// <summary>Gets or sets the active conversation's stop operation.</summary>
    internal Func<IReadOnlyList<string>, Task>? CancelRequested
    {
        get; set;
    }

    /// <summary>Closes the details when the user changes conversations.</summary>
    internal void Close()
    {
        SetArmed(null);
        Details.Close(immediately: true);
    }

    /// <summary>Draws the jobs that the active session reports.</summary>
    /// <param name="status">The accepted session status.</param>
    /// <param name="language">The reader's language.</param>
    internal void Show(SessionStatus status, InterfaceLanguage language)
    {
        var source = (status.SessionPath, status.WorkspaceRoot, language);
        if (_source is not { } previous || (previous.Path, previous.Workspace) != (source.SessionPath, source.WorkspaceRoot))
        {
            _seenFailures.Clear();
            _cleared.Clear();
            _stopping.Clear();
            Classes.Remove("live");
            Close();
        }

        BackgroundJobStatus[] jobs = [.. status.Jobs ?? [], .. (status.EndedJobs ?? []).Where(job => !_cleared.Contains(job.Id))];
        if (_source == source && _jobs.SequenceEqual(jobs))
        {
            return;
        }

        _source = source;
        _jobs = jobs;
        RenderJobs(language);
    }

    /// <summary>Formats the reported lifecycle and duration on a job row.</summary>
    /// <param name="job">One job from the kernel snapshot.</param>
    /// <param name="language">The reader's language.</param>
    /// <returns>The rendered status detail.</returns>
    private static string StatusOf(BackgroundJobStatus job, InterfaceLanguage language)
    {
        var running = job.Status == "running";
        var end = running ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : job.EndedAt;
        var elapsed = job.StartedAt > 0 && end > 0 ? Format.JobElapsed(Math.Max(0, end - job.StartedAt), language) : string.Empty;
        if (running)
        {
            return elapsed;
        }

        var key = job.Status switch
        {
            "failed" => "common.failed",
            "killed" => "plan.stopped",
            "interrupted" => "common.interrupted",
            _ => "common.done_2",
        };
        var status = MessageCatalog.Get(key, language);
        return elapsed.Length > 0 ? elapsed + " · " + status : status;
    }

    /// <summary>Uses the same type and foreground resources as the adjoining meter rail.</summary>
    /// <param name="text">The canonical or localized line.</param>
    /// <param name="size">The reference font size.</param>
    /// <param name="ink">The palette resource.</param>
    /// <returns>The rendered line.</returns>
    private static TextBlock Line(string text, int size, string ink) => new()
    {
        Text = text,
        TextTrimming = TextTrimming.CharacterEllipsis,
        [!TextBlock.FontSizeProperty] = StudioTypography.Size(size),
        [!TextBlock.ForegroundProperty] = StudioTypography.Resource(ink),
        [!TextBlock.FontFamilyProperty] = StudioTypography.Resource("ConversationUiFamily"),
    };

    /// <summary>Builds an action rendered with the job card's styles.</summary>
    /// <param name="text">The action's label.</param>
    /// <param name="action">The stable automation identifier.</param>
    /// <returns>The action button.</returns>
    private static Button ActionButton(string text, string action)
    {
        var button = new Button { Classes = { "job-deck-action" }, Content = text };
        AutomationProperties.SetAutomationId(button, action);
        return button;
    }

    /// <summary>Shows the original running, unread-failure or ended summary.</summary>
    /// <param name="language">The current language.</param>
    private void UpdateTrigger(InterfaceLanguage language)
    {
        IsVisible = _jobs.Length > 0;
        if (!IsVisible)
        {
            Classes.Remove("live");
            Classes.Remove("failed");
            Pulse.IsVisible = false;
            Close();
            return;
        }

        var running = _jobs.Count(job => job.Status == "running");
        var failed = _jobs.Count(job => job.Status == "failed" && !_seenFailures.Contains(job.Id));
        Classes.Set("live", running > 0);
        Classes.Set("failed", running == 0 && failed > 0);
        Pulse.IsVisible = running > 0;
        var key = running > 0 ? "subagent.background_jobs_running" : failed > 0 ? "subagent.background_jobs_failed" : "subagent.background_jobs_done";
        Count.Text = Format.Count(running > 0 ? running : failed > 0 ? failed : _jobs.Length, language);
        Caption.Text = MessageCatalog.Get(key, language);
        var label = MessageCatalog.Get("common.background_jobs", language);
        AutomationProperties.SetName(Trigger, label);
        ToolTip.SetTip(Trigger, label);
    }

    /// <summary>Refreshes the detail rows from the current snapshot.</summary>
    /// <param name="language">The reader's language.</param>
    private void RenderJobs(InterfaceLanguage language)
    {
        UpdateTrigger(language);
        JobsChanged?.Invoke(_jobs);
        _confirmationMotion?.Cancel();
        Rows.Children.Clear();
        _controls.Clear();
        _elapsed.Clear();
        var current = _jobs.Select(job => job.Id).ToHashSet(StringComparer.Ordinal);
        var running = _jobs.Where(job => job.Status == "running").ToArray();
        var ended = _jobs.Where(job => job.Status != "running").ToArray();
        AddSection(running, true, language);
        if (running.Length > 0 && ended.Length > 0)
        {
            Rows.Children.Add(new Border
            {
                Height = 1,
                Margin = new Thickness(0, 4, 0, 0),
                [!Border.BackgroundProperty] = StudioTypography.Resource("HairBrush"),
            });
        }

        AddSection(ended, false, language);
        if (_armed is { } armed && !_controls.ContainsKey(armed))
        {
            SetArmed(null);
        }

        if (_elapsed.Count > 0)
        {
            _clock.Start();
        }
        else
        {
            _clock.Stop();
        }

        _jobRendered.IntersectWith(current);
    }

    /// <summary>Displays the canonical running or ended jobs in the attached card.</summary>
    /// <param name="jobs">The jobs for this section.</param>
    /// <param name="running">Whether they are running.</param>
    /// <param name="language">The reader's language.</param>
    private void AddSection(BackgroundJobStatus[] jobs, bool running, InterfaceLanguage language)
    {
        if (jobs.Length == 0)
        {
            return;
        }

        var heading = Line(MessageCatalog.Get(running ? "panels.running_n" : "panels.ended_n", language, ("n", Format.Count(jobs.Length, language))), 12, "MutedBrush");
        heading.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        heading.FontWeight = FontWeight.Medium;
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 28, Margin = new Thickness(8, 0) };
        header.Children.Add(heading);
        var ids = jobs.Select(job => job.Id).ToArray();
        if (running && jobs.Length > 1 && CancelRequested is not null)
        {
            var stop = StopControl("*", ids, MessageCatalog.Get("common.stop_all", language), MessageCatalog.Get("panels.stop_all", language), language);
            Grid.SetColumn(stop, 1);
            header.Children.Add(stop);
        }
        else if (!running)
        {
            var clear = ActionButton(MessageCatalog.Get("panels.clear", language), "action:job.clear");
            clear.Click += (_, _) =>
            {
                _cleared.UnionWith(ids);
                _jobs = _jobs.Where(job => job.Status == "running" || !_cleared.Contains(job.Id)).ToArray();
                RenderJobs(language);
            };
            Grid.SetColumn(clear, 1);
            header.Children.Add(clear);
        }

        Rows.Children.Add(header);
        foreach (var job in jobs)
        {
            var row = new StackPanel { Spacing = 3 };
            var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
            var title = Line(string.IsNullOrWhiteSpace(job.Description) ? job.Label : job.Description, 13, "TextBrush");
            title.FontWeight = FontWeight.Medium;
            ToolTip.SetTip(title, title.Text);
            top.Children.Add(title);
            var time = Line(StatusOf(job, language), 12, running ? "NetInkBrush" : job.Status == "failed" ? "ErrInkBrush" : "FaintBrush");
            Grid.SetColumn(time, 1);
            top.Children.Add(time);
            row.Children.Add(top);
            if (running)
            {
                _elapsed.Add((job, time));
            }

            var command = job.Detail ?? (string.IsNullOrWhiteSpace(job.Description) ? null : job.Label);
            AddLine(command, 12, "MutedBrush");
            AddLine(job.Tail, 11, job.Status == "failed" ? "ErrInkBrush" : "FaintBrush");
            var actions = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Spacing = 4,
                Margin = new Thickness(0, 2, 0, 0),
            };
            var output = ActionButton(MessageCatalog.Get("panels.view_output", language), "action:job.view");
            output.Tag = job.Id;
            AutomationProperties.SetName(output, MessageCatalog.Get("panels.view_output_cmd", language, ("cmd", title.Text ?? string.Empty)));
            output.Click += (_, _) =>
            {
                Close();
                OutputRequested?.Invoke(job);
            };
            actions.Children.Add(output);
            if (running && CancelRequested is not null)
            {
                actions.Children.Add(StopControl(job.Id, [job.Id], MessageCatalog.Get("common.stop", language), MessageCatalog.Get("panels.stop", language), language));
            }

            row.Children.Add(actions);

            if (job.ExitCode is { } code)
            {
                AddLine(MessageCatalog.Get("panels.exit_code", language, ("code", Format.Count(code, language))), 11, code == 0 ? "FaintBrush" : "ErrInkBrush");
            }

            Rows.Children.Add(new Border { Tag = job.Id, Classes = { "job-deck-row" }, Child = row, CornerRadius = new CornerRadius(7), Padding = new Thickness(8) });
            if (_jobRendered.Add(job.Id))
            {
                EnterJobRow((Border)Rows.Children[^1]);
            }

            void AddLine(string? text, int size, string ink)
            {
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var line = Line(text, size, ink);
                    line[!TextBlock.FontFamilyProperty] = StudioTypography.Resource("StudioMonoFamily");
                    row.Children.Add(line);
                }
            }
        }
    }

    /// <summary>Builds the stop trigger and its timed confirmation in the same row.</summary>
    /// <param name="id">The confirmation identity, or the whole running group.</param>
    /// <param name="jobs">The jobs the confirmation applies to.</param>
    /// <param name="title">The initial action label.</param>
    /// <param name="question">The confirmation question.</param>
    /// <param name="language">The reader's language.</param>
    /// <returns>The row's action controls.</returns>
    private Grid StopControl(string id, string[] jobs, string title, string question, InterfaceLanguage language)
    {
        var all = id == "*";
        var request = ActionButton(title, all ? "action:job.arm-all" : "action:job.arm");
        request.Tag = id;
        request.IsEnabled = !jobs.Any(_stopping.Contains);
        request.Click += (_, _) => SetArmed(id);
        var confirm = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 6,
            Classes = { "job-deck-confirm" },
            RenderTransform = new TranslateTransform(),
        };
        var prompt = Line(question, 12, "ErrInkBrush");
        prompt.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        confirm.Children.Add(prompt);
        var accept = ActionButton(MessageCatalog.Get("panels.ok", language), all ? "action:job.cancel-all" : "action:job.cancel");
        accept.Tag = id;
        accept.Classes.Add("job-deck-sure");
        accept.Click += async (_, _) =>
        {
            if (_armed != id || CancelRequested is not { } cancel || jobs.Any(_stopping.Contains))
            {
                return;
            }

            var source = _source is { } current ? (current.Path, current.Workspace) : default;
            _stopping.UnionWith(jobs);
            SetArmed(null);
            RefreshStopButtons();
            try
            {
                await cancel(jobs).ConfigureAwait(true);
            }
            finally
            {
                if (_source is { } currentSource && (currentSource.Path, currentSource.Workspace) == source)
                {
                    _stopping.ExceptWith(jobs);
                    RefreshStopButtons();
                }
            }
        };
        confirm.Children.Add(accept);
        request.IsVisible = _armed != id;
        confirm.IsVisible = _armed == id;
        _controls.Add(id, (request, confirm, jobs));
        return new Grid { Children = { request, confirm } };
    }

    /// <summary>Applies pending requests to the latest buttons after a status refresh.</summary>
    private void RefreshStopButtons()
    {
        foreach (var (request, _, jobs) in _controls.Values)
        {
            request.IsEnabled = !jobs.Any(_stopping.Contains);
        }
    }

    /// <summary>Starts or cancels the confirmation from an explicit reader action.</summary>
    /// <param name="id">The job awaiting confirmation, if any.</param>
    private void SetArmed(string? id)
    {
        _confirmTimeout.Stop();
        _confirmationMotion?.Cancel();
        if (_armed is { } prior && _controls.TryGetValue(prior, out var old))
        {
            old.Request.IsVisible = true;
            old.Confirmation.IsVisible = false;
        }

        _armed = id;
        if (id is not null && _controls.TryGetValue(id, out var active))
        {
            active.Request.IsVisible = false;
            active.Confirmation.IsVisible = true;
            if (TopLevel.GetTopLevel(this) is Window window && window.Classes.Contains("motion"))
            {
                _ = AnimateConfirmationAsync(active.Confirmation, window);
            }

            _confirmTimeout.Start();
        }
    }

    /// <summary>Brings in the confirmation from the right without moving the rest of the card.</summary>
    /// <param name="confirmation">The visible inline confirmation.</param>
    /// <param name="window">The owner of the motion preference.</param>
    /// <returns>The confirmation entrance.</returns>
    private async Task AnimateConfirmationAsync(StackPanel confirmation, Window window)
    {
        using var lifetime = new CancellationTokenSource();
        _confirmationMotion = lifetime;
        var transform = (TranslateTransform)confirmation.RenderTransform!;
        confirmation.Opacity = 0;
        transform.X = 14;
        confirmation.PropertyChanged += Adjust;
        window.Classes.CollectionChanged += MotionChanged;
        try
        {
            var animation = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(180),
                Easing = new SplineEasing(0.16, 1, 0.3, 1),
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0d) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1d) } },
                },
            };
            await animation.RunAsync(confirmation, lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            confirmation.PropertyChanged -= Adjust;
            window.Classes.CollectionChanged -= MotionChanged;
            if (ReferenceEquals(_confirmationMotion, lifetime))
            {
                _confirmationMotion = null;
                confirmation.Opacity = 1;
                transform.X = 0;
            }
        }

        void Adjust(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == OpacityProperty)
            {
                transform.X = 14 * (1 - confirmation.Opacity);
            }
        }

        void MotionChanged(object? sender, NotifyCollectionChangedEventArgs args)
        {
            if (!window.Classes.Contains("motion"))
            {
                lifetime.Cancel();
            }
        }
    }
}
