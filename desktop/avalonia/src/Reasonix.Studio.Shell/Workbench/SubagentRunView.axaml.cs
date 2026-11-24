using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Workbench;

/// <summary>A retained workbench tab displaying a subagent's own history.</summary>
internal sealed partial class SubagentRunView : UserControl
{
    /// <summary>The owning conversation's kernel connection.</summary>
    private readonly StudioPageClient _api;

    /// <summary>The current interface language.</summary>
    private readonly InterfaceLanguage _language;

    /// <summary>The retained return action in the workbench footer.</summary>
    private readonly Button _return;

    /// <summary>The selected tab's transcript read.</summary>
    private CancellationTokenSource? _reading;

    /// <summary>The latest acknowledged run details.</summary>
    private SubagentRun _run;

    /// <summary>Whether the selected workbench canvas shows this tab.</summary>
    private bool _shown;

    /// <summary>Initializes a new instance of the <see cref="SubagentRunView"/> class.</summary>
    /// <param name="api">The owning session.</param>
    /// <param name="run">The matched run returned by the kernel.</param>
    /// <param name="language">The reader's language.</param>
    internal SubagentRunView(StudioPageClient api, SubagentRun run, InterfaceLanguage language)
    {
        InitializeComponent();
        _api = api;
        _run = run;
        _language = language;
        RunTranscript.Language = language;
        Loading.Text = MessageCatalog.Get("common.loading", language);
        TaskLabel.Text = MessageCatalog.Get("subagent.tab.task", language);
        Stop.Content = MessageCatalog.Get("subagent.tab.stop", language);
        _return = new Button
        {
            Classes = { "subagent-return" },
            Content = MessageCatalog.Get("subagent.tab.back", language),
        };
        AutomationProperties.SetAutomationId(_return, "action:agent.show-call");
        _return.Click += (_, _) =>
        {
            if (_run.ParentCall is { Length: > 0 } parent)
            {
                ReturnToCallRequested?.Invoke(parent);
            }
        };
        FooterText.Inlines!.Add(new Run(MessageCatalog.Get("subagent.tab.read_only", language) + " "));
        FooterText.Inlines.Add(new InlineUIContainer { Child = _return });
        Stop.Click += async (_, _) =>
        {
            if (_run.State != "running" || string.IsNullOrEmpty(_run.ParentCall) || StopRequested is not { } stop)
            {
                return;
            }

            Stop.IsEnabled = false;
            try
            {
                await stop(_run.ParentCall).ConfigureAwait(true);
            }
            finally
            {
                Stop.IsEnabled = true;
            }
        };
        DetachedFromVisualTree += (_, _) => SetShown(false);
        UpdateHeader();
    }

    /// <summary>Raised when the reader returns to the parent call.</summary>
    internal event Action<string>? ReturnToCallRequested;

    /// <summary>Gets or sets the action that stops the recorded parent call.</summary>
    internal Func<string, Task>? StopRequested
    {
        get; set;
    }

    /// <summary>Gets the kernel's stable identifier for this tab.</summary>
    internal string RunId => _run.Ref;

    /// <summary>Gets the displayed tab title.</summary>
    internal string TabTitle => _run.Name;

    /// <summary>Starts polling only while this run is on the workbench canvas.</summary>
    /// <param name="shown">Whether the reader can see the tab.</param>
    internal void SetShown(bool shown)
    {
        if (_shown == shown)
        {
            return;
        }

        _shown = shown;
        if (shown)
        {
            _ = ReadTranscriptAsync();
        }
        else
        {
            var reading = _reading;
            _reading = null;
            reading?.Cancel();
        }
    }

    /// <summary>Reads bounded pages and advances only to the kernel's reported cursor.</summary>
    /// <returns>The lifetime of the current tab's reads.</returns>
    private async Task ReadTranscriptAsync()
    {
        using var reading = new CancellationTokenSource();
        _reading = reading;
        var messages = new SortedDictionary<int, HistoryMessage>();
        var after = 0;
        try
        {
            while (!reading.IsCancellationRequested)
            {
                var page = await _api.ReadSubagentTranscriptAsync(RunId, after, reading.Token).ConfigureAwait(true);
                if (reading.IsCancellationRequested)
                {
                    return;
                }

                _run = page.Run;
                Loading.IsVisible = false;
                ReadError.IsVisible = false;
                UpdateHeader();
                if (after == 0 || page.Messages.Length > 0)
                {
                    foreach (var message in page.Messages)
                    {
                        messages[message.MsgIndex] = message;
                    }

                    ShowTranscript(messages.Values.ToArray(), page.Live);
                }
                else if (RunTranscript.State.Running != page.Live)
                {
                    RunTranscript.Show(RunTranscript.State with
                    {
                        Running = page.Live,
                    });
                }

                var more = page.Next > after && page.Next < page.Total;
                after = Math.Max(after, page.Next);
                if (more)
                {
                    continue;
                }

                if (!page.Live)
                {
                    return;
                }

                await Task.Delay(1500, reading.Token).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) when (reading.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (!reading.IsCancellationRequested)
            {
                Loading.IsVisible = false;
                ReadError.Text = error is KernelRefusal refusal
                    ? RefusalText.Say(refusal.Reason, refusal.Status, refusal.Detail, _language)
                    : error.Message;
                ReadError.IsVisible = true;
            }
        }
        finally
        {
            if (ReferenceEquals(_reading, reading))
            {
                _reading = null;
            }
        }
    }

    /// <summary>Uses the shared history projection and moves the first task into the header.</summary>
    /// <param name="messages">The accumulated pages.</param>
    /// <param name="live">Whether the transcript can still grow.</param>
    private void ShowTranscript(IReadOnlyList<HistoryMessage> messages, bool live)
    {
        var rows = SubagentTranscriptPage.Project(messages);
        if (rows.Count > 0 && rows[0] is UserItem task)
        {
            TaskText.Text = task.Text;
            TaskCard.IsVisible = !string.IsNullOrEmpty(task.Text);
            rows = rows.Skip(1).ToArray();
        }
        else
        {
            TaskCard.IsVisible = false;
        }

        RunTranscript.Show(new SessionState
        {
            Items = rows,
            Running = live,
        });
        RunTranscript.IsVisible = true;
        Footer.IsVisible = true;
    }

    /// <summary>Displays the model, run state and available stop action from canonical metadata.</summary>
    private void UpdateHeader()
    {
        Avatar.Show(_run.Name);
        RunTitle.Text = _run.Name;
        Model.Text = _run.Model;
        ToolTip.SetTip(Model, _run.Model);
        Stop.IsVisible = _run.State == "running" && !string.IsNullOrEmpty(_run.ParentCall) && StopRequested is not null;
        RunState.IsVisible = !Stop.IsVisible;
        _return.IsVisible = !string.IsNullOrEmpty(_run.ParentCall);
        var label = _run.State switch
        {
            "running" => "common.running",
            "completed" => "common.done_2",
            "failed" => "common.failed",
            "cancelled" => "common.cancelled",
            _ => "common.interrupted",
        };
        RunState.Text = MessageCatalog.Get(label, _language);
    }
}
