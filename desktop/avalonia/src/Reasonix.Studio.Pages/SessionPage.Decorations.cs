using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.State.Transcript;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Reasonix.Studio.Pages;

public sealed partial class SessionPage
{
    /// <summary>
    /// Draws one turn's intermediate work under a single line.
    /// </summary>
    /// <param name="work">The turn's intermediate rows.</param>
    /// <param name="first">Whether the group starts the column.</param>
    /// <param name="headerOnly">Whether the panel owns the children.</param>
    /// <returns>The fold, open or closed by the reader's own choice.</returns>
    private Control WorkFold(IReadOnlyList<TranscriptItem> work, bool first, bool headerOnly)
    {
        var tally = ActivityTally.Of(work);
        var words = work.Count(row => row is SayItem);

        var head = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
        };

        var parts = new List<string> { MessageCatalog.Get("chat.work_in_between", Language) };
        if (tally.Failed > 0)
        {
            parts.Add(MessageCatalog.Get("chat.n_failed", Language, ("n", Format.Count(tally.Failed, Language))));
        }

        parts.Add(tally.Calls > 0
            ? MessageCatalog.Get("chat.count_actions", Language, ("count", Format.Count(tally.Calls, Language)))
            : MessageCatalog.Get("chat.thinking_and_notes", Language));

        if (words > 0 && tally.Calls > 0)
        {
            parts.Add(MessageCatalog.Get("chat.notes_count", Language, ("count", Format.Count(words, Language))));
        }

        var summary = new SelectableTextBlock
        {
            Text = string.Join(" · ", parts),
            [!TextBlock.FontSizeProperty] = StudioTypography.Size(12),
            Opacity = 0.65,
            TextWrapping = TextWrapping.Wrap,
        };
        head.Children.Add(summary);

        var toggle = new Button
        {
            Content = MessageCatalog.Get(DisclosureChoices.WorkOpen ? "common.collapse" : "common.expand", Language),
            [!TextBlock.FontSizeProperty] = StudioTypography.Size(11),
            Padding = new Thickness(8, 2, 8, 2),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Grid.SetColumn(toggle, 2);
        toggle.Click += (_, _) =>
        {
            DisclosureChoices.WorkOpen = !DisclosureChoices.WorkOpen;
            Rebuild(_state.Items);
        };
        head.Children.Add(toggle);

        var body = new StackPanel
        {
            Spacing = 8,
            IsVisible = DisclosureChoices.WorkOpen,
        };
        foreach (var row in DisclosureChoices.WorkOpen && !headerOnly ? work : [])
        {
            if (row.Id == _newAnswerAt)
            {
                body.Children.Add(new NewAnswerLine(Language));
            }

            var control = RowOf(row);
            body.Children.Add(control);
        }

        var group = new StackPanel { Spacing = 6 };
        group.Children.Add(head);
        if (!headerOnly)
        {
            group.Children.Add(body);
        }

        return new Border
        {
            Child = group,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, first ? 0 : 8, 0, 0),
        };
    }

    /// <summary>Draws work under a disclosure that follows the call lifecycle.</summary>
    /// <param name="work">The canonical rows in this activity.</param>
    /// <param name="headerOnly">Whether the panel owns the children.</param>
    /// <returns>The mounted disclosure and its retained body.</returns>
    private Control ActivityGroup(IReadOnlyList<TranscriptItem> work, bool headerOnly = false)
    {
        var tally = ActivityTally.Of(work);
        var id = work[0].Id;
        var open = ActivityOpen(work, tally);
        var body = new StackPanel { Name = "ActivityBody", Spacing = 2, Margin = new Thickness(19, 2, 0, 2) };
        var disclosure = new ActivityDisclosure(
            body,
            MessageCatalog.Get("chat.count_actions", Language, ("count", Format.Count(tally.Calls, Language))),
            open,
            tally.Failed > 0 ? "ErrInkBrush" : tally.Running > 0 ? "NetBrush" : "OkInkBrush",
            value =>
            {
                RememberFold(TranscriptPart.Activity, id, value);
                if (headerOnly)
                {
                    Rebuild(_state.Items);
                }
            },
            MessageCatalog.Get("chat.activity", Language))
        {
            Tag = work,
        };
        if (!headerOnly)
        {
            disclosure.BeforeOpen = () =>
            {
                if (body.Children.Count == 0)
                {
                    foreach (var row in work)
                    {
                        var current = _indices.TryGetValue(row.Id, out var index) ? _state.Items[index] : row;
                        body.Children.Add(RowOf(current, inActivity: true));
                    }
                }
            };
            if (open || work.Any(row => row is NoticeItem or ApprovalItem or AskItem))
            {
                disclosure.BeforeOpen();
            }
        }

        disclosure.SetCountDetails(tally.Calls, ActivityDetails(tally));
        BindActivityCount(id, disclosure);
        disclosure.ConfigureLive(work.OfType<ToolItem>().LastOrDefault(tool => tool.Running), tally.Running, tally.Failed > 0, Language);
        return disclosure;
    }

    /// <summary>Determines the activity expansion from the same facts used by its header.</summary>
    /// <param name="work">Group members.</param><param name="tally">Current tool lifecycle counts.</param>
    /// <returns>Whether the group is open.</returns>
    private bool ActivityOpen(IReadOnlyList<TranscriptItem> work, ActivityTally tally) =>
        work.Any(row => row is ApprovalItem { Open: true } or AskItem { Answered: null, Outcome: null })
        || (FoldChoice(TranscriptPart.Activity, work[0].Id) ?? _folding.StartsOpen(
            TranscriptPart.Activity,
            live: !tally.Settled || work.Any(row => _liveWork.Contains(row.Id)),
            changed: work.OfType<ToolItem>().Any(tool => TranscriptFolding.ChangedFile(tool.Call))));

    /// <summary>Lets grouped reads follow the same step default as individual calls.</summary>
    /// <param name="reads">The grouped canonical reads.</param><returns>The retained reads disclosure.</returns>
    private Control ReadsDisclosure(ReadsItem reads)
    {
        var failed = reads.Tools.Any(call => !string.IsNullOrEmpty(call.Err));
        var open = FoldChoice(TranscriptPart.Steps, reads.Id) ?? _folding.StartsOpen(TranscriptPart.Steps, failed: failed);
        var body = new StackPanel { Spacing = 2, Margin = new Thickness(17, 2, 0, 2) };
        for (var index = 0; index < reads.Tools.Count; index++)
        {
            var call = reads.Tools[index];
            var id = string.IsNullOrEmpty(call.ID) ? reads.Id + ":" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) : call.ID;
            body.Children.Add(ToolRow(new ToolItem { Id = id, Call = call }, inActivity: true));
        }

        var label = MessageCatalog.Get("chat.read_n_files", Language, ("n", Format.Count(reads.Tools.Count, Language)));
        return new ActivityDisclosure(body, label, open, failed ? "ErrInkBrush" : "FaintBrush", value => RememberFold(TranscriptPart.Steps, reads.Id, value));
    }

    /// <summary>Formats the failure and elapsed-time details beside the rolling count.</summary>
    /// <param name="tally">The stretch's tally.</param>
    /// <returns>The details, including their leading separator when present.</returns>
    private string ActivityDetails(ActivityTally tally)
    {
        var details = string.Empty;
        if (tally.Failed > 0)
        {
            details += " · " + MessageCatalog.Get("chat.n_failed", Language, ("n", Format.Count(tally.Failed, Language)));
        }

        if (tally.Settled && tally.Milliseconds >= 1000)
        {
            details += " · " + MessageCatalog.Get("common.n_s", Language, ("n", Format.Count(tally.Milliseconds / 1000, Language)));
        }

        return details;
    }

    /// <summary>
    /// Draws delegates sent out side by side as one group of their own.
    /// </summary>
    /// <param name="delegates">The delegate rows, in the order they were sent.</param>
    /// <param name="first">Whether the group starts the column.</param>
    /// <param name="headerOnly">Whether the panel owns the children.</param>
    /// <returns>The group.</returns>
    private Control DelegateGroup(IReadOnlyList<ToolItem> delegates, bool first, bool headerOnly)
    {
        var running = delegates.Count(delegateRow => delegateRow.Running);
        var failed = delegates.Count(delegateRow => !delegateRow.Running && !string.IsNullOrEmpty(delegateRow.Call.Err));
        var done = delegates.Count - running - failed;

        var count = Format.Count(delegates.Count, Language);
        var settled = running == 0 && failed == 0;
        var label = settled
            ? MessageCatalog.Get("subagent.group.count", Language, ("n", count))
                + " · "
                + MessageCatalog.Get("subagent.group.all_delivered", Language, ("n", Format.Count(done, Language)))
            : MessageCatalog.Get("subagent.group.parallel", Language, ("n", count));

        var head = new SelectableTextBlock
        {
            Text = label,
            [!TextBlock.FontSizeProperty] = StudioTypography.Size(12),
            Opacity = 0.65,
        };

        var body = new StackPanel { Spacing = 4 };
        foreach (var delegateRow in headerOnly ? [] : delegates)
        {
            var control = RowOf(delegateRow);
            body.Children.Add(control);
        }

        var group = new StackPanel { Spacing = 6 };
        group.Children.Add(head);
        if (!headerOnly)
        {
            group.Children.Add(body);
        }

        return new Border
        {
            Background = Brush("RaisedBrush"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, first ? 0 : 8, 0, 0),
            Child = group,
        };
    }
}
