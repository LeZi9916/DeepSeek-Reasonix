using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System;
using System.Linq;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Advances the elapsed-time label independently of stream activity.</summary>
    private DispatcherTimer? _runClock;

    /// <summary>The start time of the turn shown by the elapsed-time label.</summary>
    private long? _runSince;

    /// <summary>Checks whether this session has a token reading to show.</summary>
    /// <param name="state">The current session snapshot.</param>
    /// <returns>Whether billed usage or streamed output is available.</returns>
    private static bool HasRunTokens(SessionState state) =>
        state.Metrics.Hit + state.Metrics.Miss + state.Metrics.Out + state.OutLive > 0;

    /// <summary>Projects the runtime and token readings into the composer's status row.</summary>
    /// <param name="state">The current session snapshot.</param>
    private void ShowComposerRunFeedback(SessionState state)
    {
        var waiting = state.Items.Any(item => item is ApprovalItem { Open: true } or AskItem { Answered: null, Outcome: null });
        var label = MessageCatalog.Get(state.Doing == "common.idle" ? MessageKey.Common.Running : state.Doing, Language);
        AutomationProperties.SetName(ComposerRunBadge, label);
        ToolTip.SetTip(ComposerRunBadge, label);
        ComposerRunBadge.IsVisible = state.Running || waiting;
        ComposerRunBadge.Classes.Set("moving", state.Running && !waiting);
        ComposerRunBadge.Classes.Set("waiting", waiting);

        var tokens = ComposerRunBadge.IsVisible && !ChangedFiles.IsVisible && HasRunTokens(state);
        ComposerRunTokens.IsVisible = tokens;
        var source = ((object?)_engine ?? Conversation, _sessionPath);
        ComposerRunTokens.Show(
            source,
            state.Metrics.Hit + state.Metrics.Miss,
            state.Metrics.Out + state.OutLive,
            state.OutLive > 0,
            Language,
            tokens && LayoutRoot.IsEffectivelyVisible && Classes.Contains("motion"));
        UpdateComposerRunClock(state.Running ? state.RunSince : null);
    }

    /// <summary>Starts the clock only when there is a running turn with a known start time.</summary>
    /// <param name="since">Start time in Unix milliseconds, if known.</param>
    private void UpdateComposerRunClock(long? since)
    {
        if (_runSince == since)
        {
            return;
        }

        _runSince = since;
        ComposerRunClock.IsVisible = since is not null;
        if (since is null)
        {
            _runClock?.Stop();
            return;
        }

        DrawComposerRunClock();
        _runClock ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => DrawComposerRunClock());
        _runClock.Start();
    }

    /// <summary>Displays the elapsed time of the active turn.</summary>
    private void DrawComposerRunClock()
    {
        if (_runSince is { } since && WindowState != WindowState.Minimized)
        {
            ComposerRunClock.Text = Format.Elapsed(Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - since));
        }
    }
}
