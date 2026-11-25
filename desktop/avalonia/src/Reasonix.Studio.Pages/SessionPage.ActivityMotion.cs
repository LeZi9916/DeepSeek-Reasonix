using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Reasonix.Studio.Pages;

public sealed partial class SessionPage
{
    /// <summary>Retains the outgoing count transform until the next layout.</summary>
    private Dictionary<string, ActivityDisclosure>? _previousActivityCounts;

    /// <summary>Records count presentation before a live update remounts the transcript.</summary>
    /// <param name="previous">The outgoing session state.</param>
    /// <returns>The visible groups that can continue their count motion.</returns>
    private Dictionary<string, ActivityDisclosure>? VisibleActivityCounts(SessionState previous) =>
        IsLiveActivityChange(previous)
            ? _rows.GetVisualDescendants().OfType<ActivityDisclosure>()
                .Where(group => group.IsEffectivelyVisible && group.Tag is IReadOnlyList<TranscriptItem>)
                .ToDictionary(group => ((IReadOnlyList<TranscriptItem>)group.Tag!)[0].Id, StringComparer.Ordinal)
            : null;

    /// <summary>Transfers the displayed count when a replacement header attaches.</summary>
    /// <param name="id">The group identity.</param><param name="disclosure">The replacement header.</param>
    private void BindActivityCount(string id, ActivityDisclosure disclosure)
    {
        if (_previousActivityCounts is not { } counts || !counts.Remove(id, out var prior))
        {
            return;
        }

        void Transfer(object? sender, Avalonia.VisualTreeAttachmentEventArgs args)
        {
            disclosure.AttachedToVisualTree -= Transfer;
            disclosure.ContinueCountMotion(prior);
        }

        disclosure.AttachedToVisualTree += Transfer;
    }

    /// <summary>Checks whether this update continues a mounted live turn that allows motion.</summary>
    /// <param name="previous">The outgoing session state.</param>
    /// <returns>Whether a live transition is eligible.</returns>
    private bool IsLiveActivityChange(SessionState previous) =>
        previous.RunSince is not null && previous.Running && previous.RunSince == _state.RunSince
        && TopLevel.GetTopLevel(this) is Window window && window.Classes.Contains("motion");

    /// <summary>Animates live tool changes once, independently of a transcript rebuild.</summary>
    /// <param name="previous">The previous visible session state.</param>
    private void AnimateActivityChanges(SessionState previous)
    {
        if (!IsLiveActivityChange(previous))
        {
            return;
        }

        var presented = _state;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (ReferenceEquals(_state, presented))
                {
                    PlayPresentedActivity(previous);
                }
            },
            DispatcherPriority.Loaded);
    }

    /// <summary>Applies the captured live transitions to the mounted activity rows.</summary>
    /// <param name="previous">The state before the live change.</param>
    private void PlayPresentedActivity(SessionState previous)
    {
        var before = previous.Items.OfType<ToolItem>().ToDictionary(tool => tool.Id, StringComparer.Ordinal);
        var order = 0;
        foreach (var row in Drawn)
        {
            if (row is not Border { Tag: ToolItem tool, Child: ToolCallHeader header } || !row.IsEffectivelyVisible)
            {
                continue;
            }

            if (!before.TryGetValue(tool.Id, out var prior))
            {
                if (tool.Running)
                {
                    header.EnterActivity(order++);
                }

                continue;
            }

            var failed = !string.IsNullOrEmpty(tool.Call.Err);
            var returned = prior.Running && !tool.Running;
            if (failed ? string.IsNullOrEmpty(prior.Call.Err) : returned)
            {
                header.SettleActivity(failed, returned);
            }
        }
    }
}
