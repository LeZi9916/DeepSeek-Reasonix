using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Reasonix.Studio.Contracts.Jobs;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.State.Transcript;
using System;

namespace Reasonix.Studio.Pages;

internal sealed partial class ToolCallHeader
{
    /// <summary>The language used for the job labels.</summary>
    private InterfaceLanguage _backgroundLanguage;

    /// <summary>Raised when the reader opens the job linked to this tool result.</summary>
    internal event Action<string>? BackgroundOutputRequested;

    /// <summary>Gets the job identity explicitly reported by the kernel.</summary>
    internal string? BackgroundJobId
    {
        get;
        private set;
    }

    /// <summary>Shows the job's own duration and lifecycle without changing the shell call's settled state.</summary>
    /// <param name="job">The current job, if it remains in the conversation's job list.</param>
    internal void ShowBackgroundJob(BackgroundJobStatus? job)
    {
        var live = job?.Status == "running";
        var note = this.FindControl<TextBlock>("JobNote")!;
        var reading = this.FindControl<TextBlock>("JobReading")!;
        note.IsVisible = live;
        var label = string.Empty;
        if (job is not null)
        {
            var end = live ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : job.EndedAt;
            var elapsed = end > 0 && job.StartedAt > 0 ? Format.JobElapsed(Math.Max(0, end - job.StartedAt), _backgroundLanguage) : string.Empty;
            if (live)
            {
                label = MessageCatalog.Get("chat.background_running", _backgroundLanguage, ("time", elapsed));
            }
            else
            {
                var exit = job.ExitCode is { } code
                    ? MessageCatalog.Get("panels.exit_code", _backgroundLanguage, ("code", Format.Count(code, _backgroundLanguage)))
                    : string.Empty;
                label = elapsed.Length > 0 && exit.Length > 0 ? elapsed + " · " + exit : elapsed + exit;
            }
        }

        reading.Classes.Set("live", live);
        reading.Classes.Set("failed", job?.Status == "failed");
        reading.Text = label;
        reading.IsVisible = label.Length > 0;
    }

    /// <summary>Configures the supplementary job labels without changing the tool's result disclosure.</summary>
    /// <param name="tool">The canonical tool call.</param>
    /// <param name="language">Current interface language.</param>
    /// <param name="inActivity">Whether the tool uses the activity row layout.</param>
    private void InitializeBackgroundJob(ToolItem tool, InterfaceLanguage language, bool inActivity)
    {
        if (tool.Call.Execution is not { State: "background_started", JobID: { Length: > 0 } id })
        {
            return;
        }

        BackgroundJobId = id;
        _backgroundLanguage = language;
        var reading = this.FindControl<TextBlock>("JobReading")!;
        var grid = this.FindControl<Grid>("HeaderGrid")!;
        grid.ColumnDefinitions = new ColumnDefinitions(inActivity ? "14,Auto,*,Auto,14" : "14,Auto,*,Auto,14,10");
        Grid.SetColumn(this.FindControl<Grid>("StatusSlot")!, 4);
        Grid.SetColumn(this.FindControl<Border>("Chevron")!, 5);
        if (inActivity)
        {
            this.FindControl<TextBlock>("ToolName")!.Text = MessageCatalog.Get("chat.sent_to_background", language);
            reading.Margin = new Avalonia.Thickness(0, 0, 36, 0);
            var mark = this.FindControl<Viewbox>("StaticStatus")!;
            mark.Width = mark.Height = 14;
        }

        var note = this.FindControl<TextBlock>("JobNote")!;
        var text = MessageCatalog.Get("chat.background_note", language);
        note.Inlines!.Add(new Run(text));
        var link = new Button { Classes = { "background-output-link" }, Content = MessageCatalog.Get("panels.view_output", language) };
        AutomationProperties.SetAutomationId(link, "action:job.view");
        link.Tag = id;
        link.Click += (_, _) => BackgroundOutputRequested?.Invoke(id);
        note.Inlines.Add(new InlineUIContainer { Child = link, BaselineAlignment = BaselineAlignment.Center });
    }
}
