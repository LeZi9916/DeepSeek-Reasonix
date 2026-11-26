using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Reasonix.Studio.Pages;

public sealed partial class SessionPage
{
    /// <summary>The maximum visual body hosted inside one disclosure.</summary>
    private const int InlineChildren = 24;

    /// <summary>Large transcripts use the live viewport and bounded detached-view reuse.</summary>
    private const int LargeTranscriptThreshold = 120;

    /// <summary>Contains descriptions only, allowing the panel to recycle visual containers.</summary>
    private readonly AvaloniaList<ViewportRow> _viewportRows = [];

    /// <summary>Maps a canonical row to its virtual container.</summary>
    private readonly Dictionary<string, int> _locations = new(StringComparer.Ordinal);

    /// <summary>Maps a stable row identity to its current state position.</summary>
    private readonly Dictionary<string, int> _indices = new(StringComparer.Ordinal);

    /// <summary>Contains only rows whose controls are attached.</summary>
    private readonly Dictionary<string, Control> _realized = new(StringComparer.Ordinal);

    /// <summary>In-flight disclosures that the virtual panel is replacing.</summary>
    private readonly Dictionary<(TranscriptPart Part, string Id), DisclosureMotion.Pose> _foldPoses = [];

    /// <summary>Invalidates deferred work after structural changes.</summary>
    private long _viewportVersion;

    /// <summary>Lets an explicit jump supersede a queued scroll restoration.</summary>
    private long _anchorTicket;

    /// <summary>Identifies the shape of a virtual container.</summary>
    private enum RowKind
    {
        /// <summary>Prose container.</summary>
        Prose,

        /// <summary>Separator container.</summary>
        Separator,

        /// <summary>Unread container.</summary>
        Unread,

        /// <summary>Work container.</summary>
        Work,

        /// <summary>Activity container.</summary>
        Activity,

        /// <summary>Delegates container.</summary>
        Delegates,

        /// <summary>An expanded member measured independently.</summary>
        ActivityMember,

        /// <summary>A member of a large work fold.</summary>
        WorkMember,

        /// <summary>A member of a large delegate group.</summary>
        DelegateMember,

        /// <summary>Wait container.</summary>
        Wait,
    }

    /// <summary>Gets attached rows in transcript order.</summary>
    private IEnumerable<Control> Drawn => _realized.OrderBy(pair => _indices.GetValueOrDefault(pair.Key)).Select(pair => pair.Value);

    /// <summary>Captures the first visible row and its viewport-relative offset.</summary>
    /// <returns>The anchor or the current scroll offset as a fallback.</returns>
    public (string? Id, double Offset, double Inset) ReadAnchor()
    {
        foreach (var (id, row) in _realized.OrderBy(pair => _indices.GetValueOrDefault(pair.Key)))
        {
            if (row.IsEffectivelyVisible && row.TranslatePoint(default, _scroll) is { } point && point.Y < _scroll.Viewport.Height && point.Y + row.Bounds.Height > 0)
            {
                return (id, _scroll.Offset.Y, point.Y);
            }
        }

        return (null, _scroll.Offset.Y, 0);
    }

    /// <summary>Restores a reader's position after the virtual panel applies a projection.</summary>
    /// <param name="anchor">Row and position from before the change.</param>
    /// <param name="applied">Called after the scroll position is committed.</param>
    public void RestoreAnchor((string? Id, double Offset, double Inset) anchor, Action? applied = null)
    {
        var version = _viewportVersion;
        var ticket = ++_anchorTicket;
        Dispatcher.UIThread.Post(() => Restore(realize: true), DispatcherPriority.Loaded);

        void Restore(bool realize)
        {
            if (version != _viewportVersion || ticket != _anchorTicket)
            {
                return;
            }

            if (anchor.Id is { } id && _locations.TryGetValue(id, out var index))
            {
                if (_realized.GetValueOrDefault(id)?.TranslatePoint(default, _scroll) is { } point)
                {
                    _scroll.Offset = _scroll.Offset.WithY(_scroll.Offset.Y + point.Y - anchor.Inset);
                    applied?.Invoke();
                    return;
                }

                if (realize)
                {
                    _rows.ScrollIntoView(index);
                    Dispatcher.UIThread.Post(() => Restore(realize: false), DispatcherPriority.Loaded);
                    return;
                }
            }

            _scroll.Offset = _scroll.Offset.WithY(anchor.Offset);
            applied?.Invoke();
        }
    }

    /// <summary>Tests whether an entry retains its members and decoration.</summary>
    /// <param name="left">Mounted descriptor.</param><param name="right">New descriptor.</param>
    /// <returns>Whether the existing container still represents the entry.</returns>
    private static bool SameEntry(ViewportRow left, ViewportRow right)
    {
        if (left.Id != right.Id || left.Decoration != right.Decoration || left.First != right.First || left.Expanded != right.Expanded
            || !ReferenceEquals(left.Lead, right.Lead) || left.Children.Count != right.Children.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Children.Count; i++)
        {
            if (!ReferenceEquals(left.Children[i], right.Children[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Copies the work members exactly once during projection.</summary>
    /// <param name="rows">The source lane.</param><param name="start">First member.</param><param name="end">Exclusive end.</param>
    /// <returns>The copied members.</returns>
    private static TranscriptItem[] Slice(IReadOnlyList<TranscriptItem> rows, int start, int end)
    {
        var slice = new TranscriptItem[end - start];
        for (var i = 0; i < slice.Length; i++)
        {
            slice[i] = rows[start + i];
        }

        return slice;
    }

    /// <summary>Follows one state update at the tail without reacting to the panel's extent estimates.</summary>
    private void FollowEnd()
    {
        var ticket = ++_anchorTicket;
        var offset = _scroll.Offset.Y;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (ticket == _anchorTicket && (_followingLatest || _scroll.Offset.Y >= offset - 2) && _viewportRows.Count > 0)
                {
                    _rows.ScrollIntoView(_viewportRows.Count - 1);
                    _scroll.ScrollToEnd();
                    UpdateTranscriptJump();
                }
            },
            DispatcherPriority.Loaded);
    }

    /// <summary>Projects the complete history into a collection of lightweight entries.</summary>
    /// <param name="items">Canonical visible history.</param><param name="recreate">Whether mounted views are stale.</param>
    private void ProjectViewport(IReadOnlyList<TranscriptItem> items, bool recreate)
    {
        _viewportVersion++;
        var next = new List<ViewportRow>();
        foreach (var lane in TranscriptLanes.Split(items))
        {
            if (lane.IsDelegateGroup)
            {
                AddGroup(lane.Delegates, RowKind.Delegates, next);
            }
            else if (!FoldsTurnWork)
            {
                AddInline(lane.Rows, next);
            }
            else
            {
                foreach (var turn in TurnSegment.Split(lane.Rows))
                {
                    if (turn.Lead is { } user)
                    {
                        AddProse(user, [], next);
                    }

                    if (turn.HasWork)
                    {
                        AddGroup(turn.Work, RowKind.Work, next);
                    }

                    AddInline(turn.Tail, next);
                }
            }
        }

        if (_state.ResponseWait is not null)
        {
            next.Add(new ViewportRow("wait", null, [], RowKind.Wait, false));
        }

        if (_rows.ItemsPanelRoot is VirtualizingStackPanel panel)
        {
            panel.CacheLength = next.Count <= LargeTranscriptThreshold ? 0.5 : 0.1;
        }

        _indices.Clear();
        _locations.Clear();
        for (var i = 0; i < items.Count; i++)
        {
            _indices[items[i].Id] = i;
        }

        for (var i = 0; i < next.Count; i++)
        {
            var entry = next[i];
            if (entry.Lead is { } lead)
            {
                _locations[lead.Id] = i;
            }

            foreach (var child in entry.Children)
            {
                _locations[child.Id] = i;
            }
        }

        if (recreate)
        {
            _viewportRows.Clear();
            _rows.ClearRecent();
            _viewportRows.AddRange(next);
            return;
        }

        var prefix = 0;
        while (prefix < next.Count && prefix < _viewportRows.Count && SameEntry(_viewportRows[prefix], next[prefix]))
        {
            prefix++;
        }

        var oldEnd = _viewportRows.Count;
        var newEnd = next.Count;
        while (oldEnd > prefix && newEnd > prefix && SameEntry(_viewportRows[oldEnd - 1], next[newEnd - 1]))
        {
            oldEnd--;
            newEnd--;
        }

        if (oldEnd - prefix == newEnd - prefix)
        {
            var captured = false;
            for (var i = prefix; i < newEnd; i++)
            {
                if (!SameEntry(_viewportRows[i], next[i]) && !TryRetainActivityEntry(_viewportRows[i], next[i]))
                {
                    if (!captured)
                    {
                        CaptureFoldPoses();
                        captured = true;
                    }

                    _viewportRows[i] = next[i];
                }
            }

            return;
        }

        CaptureFoldPoses();
        _viewportRows.RemoveRange(prefix, oldEnd - prefix);
        _viewportRows.InsertRange(prefix, next.GetRange(prefix, newEnd - prefix));
    }

    /// <summary>Retains a visible in-flight fold when its projected row must be replaced.</summary>
    private void CaptureFoldPoses()
    {
        _foldPoses.Clear();
        var version = _viewportVersion;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_viewportVersion == version)
                {
                    _foldPoses.Clear();
                }
            },
            DispatcherPriority.Background);

        foreach (var (id, row) in _realized)
        {
            if (row is Border { Child: ToolCallHeader tool } && tool.FindControl<DisclosureMotion>("Detail")?.CapturePose() is { } pose)
            {
                _foldPoses[(TranscriptPart.Steps, id)] = pose;
            }
        }

        foreach (var group in _rows.GetVisualDescendants().OfType<ActivityDisclosure>())
        {
            if (group.Tag is IReadOnlyList<TranscriptItem> work && group.FindControl<DisclosureMotion>("Body")?.CapturePose() is { } pose)
            {
                _foldPoses[(TranscriptPart.Activity, work[0].Id)] = pose;
            }
        }
    }

    /// <summary>Transfers an interrupted fold to the replacement for the same transcript record.</summary>
    /// <param name="part">The kind of disclosure.</param><param name="id">Its canonical record.</param>
    /// <param name="body">The replacement body, before it is mounted.</param>
    private void RestoreFoldPose(TranscriptPart part, string id, DisclosureMotion body)
    {
        if (_foldPoses.Remove((part, id), out var pose))
        {
            body.RestorePose(pose);
        }
    }

    /// <summary>Groups each run of work with the preceding answer when present.</summary>
    /// <param name="rows">The ordinary transcript lane.</param><param name="output">The projected entries.</param>
    private void AddInline(IReadOnlyList<TranscriptItem> rows, List<ViewportRow> output)
    {
        for (var i = 0; i < rows.Count;)
        {
            var row = rows[i];
            if (row is UserItem)
            {
                AddProse(row, [], output);
                i++;
                continue;
            }

            var answer = row as SayItem;
            var start = answer is null ? i : i + 1;
            i = start;
            while (i < rows.Count && rows[i] is not (SayItem or UserItem))
            {
                i++;
            }

            var work = Slice(rows, start, i);
            if (answer is null)
            {
                AddGroup(work, RowKind.Activity, output);
            }
            else if (work.Length > InlineChildren)
            {
                AddProse(answer, [], output);
                AddGroup(work, RowKind.Activity, output);
            }
            else
            {
                AddProse(answer, work, output);
            }
        }
    }

    /// <summary>Projects large groups as one header and independently measured members.</summary>
    /// <param name="group">Canonical group members.</param><param name="kind">Header kind.</param>
    /// <param name="output">Projected entries.</param>
    private void AddGroup(IReadOnlyList<TranscriptItem> group, RowKind kind, List<ViewportRow> output)
    {
        output.Add(new ViewportRow(kind.ToString() + ":" + group[0].Id, null, group, kind, output.Count == 0, kind == RowKind.Work && DisclosureChoices.WorkOpen));
        if (group.Count <= InlineChildren)
        {
            return;
        }

        var children = kind switch
        {
            RowKind.Activity when ActivityOpen(group, ActivityTally.Of(group)) => RowKind.ActivityMember,
            RowKind.Work when DisclosureChoices.WorkOpen => RowKind.WorkMember,
            RowKind.Delegates => RowKind.DelegateMember,
            _ => kind,
        };
        if (children == kind)
        {
            return;
        }

        for (var i = 0; i < group.Count; i++)
        {
            var row = group[i];
            output.Add(new ViewportRow(row.Id, row, [], children, i == 0));
        }
    }

    /// <summary>Adds one prose entry, including its preceding separator.</summary>
    /// <param name="lead">The prose row.</param><param name="work">Its attached activity.</param><param name="output">Projected entries.</param>
    private void AddProse(TranscriptItem lead, IReadOnlyList<TranscriptItem> work, List<ViewportRow> output)
    {
        var kind = lead.Id == _newAnswerAt ? RowKind.Unread : lead is UserItem && output.Count > 0 ? RowKind.Separator : RowKind.Prose;
        output.Add(new ViewportRow(lead.Id, lead, work, kind, output.Count == 0));
    }

    /// <summary>Builds a view only when a virtual container requests it.</summary>
    /// <param name="entry">Projected entry.</param>
    /// <returns>The current visual representation.</returns>
    private Control Realize(ViewportRow entry)
    {
        if (entry.Lead is { } lead)
        {
            var current = _indices.TryGetValue(lead.Id, out var index) ? _state.Items[index] : lead;
            var row = RowOf(current, inActivity: entry.Decoration == RowKind.ActivityMember);
            switch (entry.Decoration)
            {
                case RowKind.ActivityMember:
                    row.Margin = new Thickness(19, 2, 0, 0);
                    return row;
                case RowKind.DelegateMember:
                    return new Border
                    {
                        Child = row,
                        Background = Brush("RaisedBrush"),
                        Padding = new Thickness(12, 0, 12, 4),
                    };
                case RowKind.WorkMember:
                    return lead.Id == _newAnswerAt
                        ? new StackPanel { Spacing = 8, Children = { new NewAnswerLine(Language), row } }
                        : row;
            }

            if (row is Border { Child: AnswerCard answer } && entry.Children.Count > 0)
            {
                answer.SetActivity(ActivityGroup(entry.Children));
            }

            if (entry.Decoration == RowKind.Unread)
            {
                if (lead is UserItem)
                {
                    row.Margin = default;
                }

                return new StackPanel { Children = { new NewAnswerLine(Language), row } };
            }

            if (entry.Decoration == RowKind.Separator)
            {
                var line = new Border { Height = 1, Background = Brush("BorderBrush"), Margin = new Thickness(0, 32, 0, 12) };
                return new StackPanel { Children = { line, row } };
            }

            return row;
        }

        switch (entry.Decoration)
        {
            case RowKind.Work:
                return WorkFold(entry.Children, entry.First, entry.Children.Count > InlineChildren);
            case RowKind.Delegates:
                return DelegateGroup((IReadOnlyList<ToolItem>)entry.Children, entry.First, entry.Children.Count > InlineChildren);
            case RowKind.Activity:
                return ActivityGroup(entry.Children, entry.Children.Count > InlineChildren);
            case RowKind.Wait:
                _responseWait.Show(_state.ResponseWait, Language);
                return _responseWait;
            default:
                throw new InvalidOperationException($"Missing transcript content for {entry.Decoration}.");
        }
    }

    /// <summary>Registers the mounted control without keeping recycled rows alive.</summary>
    /// <param name="id">Canonical row identity.</param><param name="row">New row.</param>
    private void RegisterRow(string id, Control row)
    {
        row.AttachedToVisualTree += (_, _) => _realized[id] = row;
        row.DetachedFromVisualTree += (_, _) =>
        {
            if (_realized.TryGetValue(id, out var held) && ReferenceEquals(held, row))
            {
                _realized.Remove(id);
            }
        };
    }

    /// <summary>Returns the row's mounted control, bringing its container into view if needed.</summary>
    /// <param name="id">The canonical row identity.</param>
    /// <returns>The attached row when its disclosure is open.</returns>
    private Control? Locate(string id)
    {
        _anchorTicket++;
        if (!_realized.ContainsKey(id) && _locations.TryGetValue(id, out var index))
        {
            var extent = _scroll.Extent.Height;
            _rows.ScrollIntoView(index);

            // A distant jump can revise the estimated extent before the target is realized.
            if (_rows.ContainerFromIndex(index) is null && _scroll.Extent.Height != extent)
            {
                _rows.ScrollIntoView(index);
            }
        }

        return _realized.GetValueOrDefault(id);
    }

    /// <summary>Reconciles the descriptors delivered to the virtualizing panel.</summary>
    /// <param name="items">Canonical transcript rows.</param>
    /// <param name="recreate">Whether a settings change invalidates mounted controls.</param>
    private void Rebuild(IReadOnlyList<TranscriptItem> items, bool recreate = false)
    {
        if (_forms.Count > 0)
        {
            var open = items.OfType<AskItem>().Where(ask => ask.Answered is null).Select(ask => ask.Ask.ID).ToHashSet();
            foreach (var id in _forms.Keys.Where(id => !open.Contains(id)).ToArray())
            {
                _forms.Remove(id);
            }
        }

        var anchor = ReadAnchor();
        var following = _followingLatest || (_scroll.ScrollBarMaximum.Y > 0 && _scroll.Offset.Y >= _scroll.ScrollBarMaximum.Y - 2);
        GatherLiveWork(items);
        ProjectViewport(items, recreate);
        RefreshBackgroundTools();
        RefreshMessageRail();
        if (!following)
        {
            RestoreAnchor(anchor);
        }
    }

    /// <summary>Uses the mounted answer when the canonical row identity survives an update.</summary>
    /// <param name="previousState">The previous state.</param>
    /// <param name="nextState">The acknowledged state.</param>
    /// <returns>Whether the list's structural projection can be retained.</returns>
    private bool UpdateMountedRows(SessionState previousState, SessionState nextState)
    {
        var previous = previousState.Items;
        var next = nextState.Items;
        if (previous.Count != next.Count)
        {
            return false;
        }

        if (previousState.Revision == nextState.Revision && nextState.StreamingRow is int at
            && (uint)at < (uint)next.Count && next[at] is SayItem streamed
            && previous[at] is SayItem outgoing && streamed.Id == outgoing.Id)
        {
            UpdateAnswer(streamed);
            return true;
        }

        var tools = new List<ToolItem>();
        for (var i = 0; i < next.Count; i++)
        {
            if (ReferenceEquals(previous[i], next[i]))
            {
                continue;
            }

            switch (previous[i], next[i])
            {
                case (SayItem before, SayItem after) when before.Id == after.Id:
                    UpdateAnswer(after);
                    break;
                case (ToolItem before, ToolItem after) when CanRetainTool(before, after):
                    tools.Add(after);
                    break;
                default:
                    return false;
            }
        }

        if (tools.Count > 0)
        {
            UpdateMountedTools(tools);
        }

        return true;
    }

    /// <summary>Updates a realized answer while its descriptor keeps the row identity.</summary>
    /// <param name="item">Current answer state.</param>
    private void UpdateAnswer(SayItem item)
    {
        if (_realized.TryGetValue(item.Id, out var row) && row is Border { Child: AnswerCard answer })
        {
            row.Tag = item;
            answer.Update(item, Language);
        }
    }

    /// <summary>One virtual container or decoration in the reading column.</summary>
    /// <param name="Id">Stable descriptor identity.</param><param name="Lead">Optional prose.</param>
    /// <param name="Children">Canonical work appearing in the container.</param><param name="Decoration">The view to create.</param>
    /// <param name="First">Whether the entry starts the reading column.</param>
    /// <param name="Expanded">Whether a work fold is open.</param>
    private sealed record ViewportRow(string Id, TranscriptItem? Lead, IReadOnlyList<TranscriptItem> Children, RowKind Decoration, bool First, bool Expanded = false)
    {
        /// <summary>Gets or sets the current members of the retained descriptor.</summary>
        public IReadOnlyList<TranscriptItem> Children { get; set; } = Children;
    }
}
