using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.State.Transcript;
using System;

namespace Reasonix.Studio.Shell.Composer;

/// <summary>A retained line for one dispatch in the subagent deck.</summary>
internal sealed partial class SubagentDeckRow : UserControl
{
    /// <summary>The most recent activity of this dispatch.</summary>
    private SubagentActivity? _activity;

    /// <summary>The reader's language.</summary>
    private InterfaceLanguage _language;

    /// <summary>When the current running reading first appeared in the open deck.</summary>
    private long _shownSince;

    /// <summary>Initializes a new instance of the <see cref="SubagentDeckRow"/> class.</summary>
    public SubagentDeckRow()
    {
        InitializeComponent();
        Open.Click += (_, _) =>
        {
            if (_activity is { } activity)
            {
                TranscriptRequested?.Invoke(activity);
            }
        };
    }

    /// <summary>Raised when the reader opens this dispatch's transcript.</summary>
    internal event Action<SubagentActivity>? TranscriptRequested;

    /// <summary>Projects the dispatch without replacing its avatar or animated mark.</summary>
    /// <param name="activity">The current reading.</param>
    /// <param name="language">The reader's language.</param>
    internal void Show(SubagentActivity activity, InterfaceLanguage language)
    {
        if (_activity == activity && _language == language)
        {
            return;
        }

        if (_activity?.Running != activity.Running)
        {
            _shownSince = 0;
        }

        _activity = activity;
        _language = language;
        var profile = activity.Call.Call.Profile;
        var name = string.IsNullOrEmpty(profile?.Name) ? "task" : profile.Name;
        Avatar.Show(name);
        AgentName.Text = name;
        AgentLabel.Text = profile?.Label;
        AgentLabel.IsVisible = !string.IsNullOrEmpty(profile?.Label);
        var tool = activity.Call.Call;
        Open.Tag = activity.Call.Id;
        AutomationProperties.SetName(Open, MessageCatalog.Get("subagent.deck.open_full", language, ("name", name)));
        ToolTip.SetTip(Open, activity.Running || string.IsNullOrEmpty(tool.Err) ? null : tool.Err);
        var failed = !activity.Running && (!string.IsNullOrEmpty(tool.Err) || !string.IsNullOrEmpty(tool.RefusalCode));
        Reading[!TextBlock.ForegroundProperty] = StudioTypography.Resource(activity.Running ? "NetInkBrush" : failed ? "ErrInkBrush" : "MutedBrush");
        StepMark.IsVisible = activity.Running;
        Tick();
    }

    /// <summary>Starts the watched duration when the details become visible.</summary>
    internal void BeginWatching()
    {
        _shownSince = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Tick();
    }

    /// <summary>Updates the running duration and step reading.</summary>
    internal void Tick()
    {
        if (_activity is not { } activity)
        {
            return;
        }

        var tool = activity.Call.Call;
        if (activity.Running)
        {
            var step = MessageCatalog.Get("subagent.step", _language, ("n", Format.Count(Math.Max(1, activity.Call.Children.Count), _language)));
            var elapsed = _shownSince == 0 ? 0 : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _shownSince;
            Reading.Text = elapsed >= 1000 ? step + " · " + Format.Elapsed(elapsed) : step;
        }
        else
        {
            var status = !string.IsNullOrEmpty(tool.RefusalCode) ? "subagent.state.not_run" : !string.IsNullOrEmpty(tool.Err) ? "common.failed" : "subagent.state.delivered";
            var label = MessageCatalog.Get(status, _language);
            Reading.Text = tool.DurationMs > 0 ? label + " · " + Format.Elapsed(tool.DurationMs) : label;
        }
    }
}
