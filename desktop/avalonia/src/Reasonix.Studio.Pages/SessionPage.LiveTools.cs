using Avalonia.Controls;
using Avalonia.VisualTree;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.State.Transcript;
using System.Collections.Generic;
using System.Linq;

namespace Reasonix.Studio.Pages;

public sealed partial class SessionPage
{
    /// <summary>Checks whether the new snapshot still uses the mounted tool template.</summary>
    /// <param name="before">The tool formerly shown.</param><param name="after">The current tool.</param>
    /// <returns>Whether presentation can update in place.</returns>
    private static bool CanRetainTool(ToolItem before, ToolItem after) =>
        before.Id == after.Id && before.Hidden <= 0 && after.Hidden <= 0 && before.Call.Name == after.Call.Name
        && Equals(before.Call.Profile, after.Call.Profile)
        && before.Call.Execution?.JobID == after.Call.Execution?.JobID
        && before.Call.Execution?.State == after.Call.Execution?.State;

    /// <summary>Updates the tool output and its enclosing activity reading.</summary>
    /// <param name="tools">All changes in the current projection.</param>
    private void UpdateMountedTools(IReadOnlyList<ToolItem> tools)
    {
        var groups = new HashSet<ActivityDisclosure>();
        foreach (var tool in tools)
        {
            if (_realized.GetValueOrDefault(tool.Id) is not Border { Child: ToolCallHeader header } row)
            {
                continue;
            }

            row.Tag = tool;
            header.UpdateTool(tool, Language);
            if (row.GetVisualAncestors().OfType<ActivityDisclosure>().FirstOrDefault(group => group.Tag is IReadOnlyList<TranscriptItem>) is { } owner)
            {
                groups.Add(owner);
            }
        }

        foreach (var group in groups)
        {
            var current = ReadCurrentWork((IReadOnlyList<TranscriptItem>)group.Tag!);
            var tally = ActivityTally.Of(current);
            group.Tag = current;
            group.UpdateCount(tally.Calls, MessageCatalog.Get("chat.count_actions", Language, ("count", Format.Count(tally.Calls, Language))), ActivityDetails(tally), animate: false);
            group.ConfigureLive(current.OfType<ToolItem>().LastOrDefault(tool => tool.Running), tally.Running, tally.Failed > 0, Language);
        }
    }

    /// <summary>Appends new siblings inside a mounted activity without detaching open tools.</summary>
    /// <param name="before">The retained visual descriptor.</param><param name="after">The new descriptor.</param>
    /// <returns>Whether the group contained the same existing rows in the same order.</returns>
    private bool TryRetainActivityEntry(ViewportRow before, ViewportRow after)
    {
        if (before.Id != after.Id || before.Decoration != after.Decoration || before.First != after.First
            || !ReferenceEquals(before.Lead, after.Lead) || before.Children.Count == 0
            || after.Children.Count <= before.Children.Count || after.Children.Count > InlineChildren
            || before.Decoration is not (RowKind.Activity or RowKind.Prose))
        {
            return false;
        }

        var updates = new List<ToolItem>();
        for (var index = 0; index < before.Children.Count; index++)
        {
            var old = before.Children[index];
            var next = after.Children[index];
            if (!ReferenceEquals(old, next))
            {
                if (old is not ToolItem prior || next is not ToolItem current || !CanRetainTool(prior, current))
                {
                    return false;
                }

                updates.Add(current);
            }
        }

        var groupId = before.Children[0].Id;
        var group = _rows.GetVisualDescendants().OfType<ActivityDisclosure>()
            .FirstOrDefault(view => view.Tag is IReadOnlyList<TranscriptItem> work && work[0].Id == groupId);
        if (group?.GetVisualDescendants().OfType<StackPanel>().FirstOrDefault(panel => panel.Name == "ActivityBody") is not { } body)
        {
            return false;
        }

        var previousCount = before.Children.Count;
        before.Children = after.Children;
        var tally = ActivityTally.Of(after.Children);
        group.Tag = after.Children;
        if (body.Children.Count > 0)
        {
            foreach (var child in after.Children.Skip(previousCount))
            {
                body.Children.Add(RowOf(child, inActivity: true));
            }
        }

        group.UpdateCount(tally.Calls, MessageCatalog.Get("chat.count_actions", Language, ("count", Format.Count(tally.Calls, Language))), ActivityDetails(tally), animate: _state.Running && !_state.IsRestored);
        group.ConfigureLive(after.Children.OfType<ToolItem>().LastOrDefault(tool => tool.Running), tally.Running, tally.Failed > 0, Language);
        group.SetOpen(ActivityOpen(after.Children, tally));
        if (updates.Count > 0)
        {
            UpdateMountedTools(updates);
        }

        return true;
    }

    /// <summary>Resolves retained projection entries against the currently acknowledged transcript.</summary>
    /// <param name="work">The work represented by this container.</param>
    /// <returns>The current work, in the existing order.</returns>
    private TranscriptItem[] ReadCurrentWork(IReadOnlyList<TranscriptItem> work) =>
        work.Select(item => _indices.TryGetValue(item.Id, out var index) ? _state.Items[index] : item).ToArray();
}
