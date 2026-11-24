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
    /// <summary>Draws transcript lanes in their original order.</summary>
    /// <param name="items">The rows to draw.</param>
    private void DrawRows(IReadOnlyList<TranscriptItem> items)
    {
        foreach (var lane in TranscriptLanes.Split(items))
        {
            if (lane.IsDelegateGroup)
            {
                AddRaw(DelegateGroup(lane.Delegates));
            }
            else
            {
                DrawOrdinary(lane.Rows);
            }
        }
    }

    /// <summary>
    /// Draws the ordinary rows of one lane, gathering a stretch of settled work
    /// under one activity group.
    /// </summary>
    /// <param name="rows">The lane's rows.</param>
    /// <remarks>
    /// A group is a run of work between two pieces of prose: it starts after the
    /// last answer and ends at the next one. Reading it that way rather than by a
    /// fixed size is what keeps one turn's work together however many calls it
    /// made, and keeps two turns from sharing a head.
    ///
    /// When the reader has asked for a turn's work to fold, each turn's
    /// intermediates go under one line first, and what is inside that line is
    /// drawn exactly as it would be inline — the setting decides where the work
    /// starts closed, never how it is drawn.
    /// </remarks>
    private void DrawOrdinary(IReadOnlyList<TranscriptItem> rows)
    {
        if (!FoldsTurnWork)
        {
            DrawInline(rows);
            return;
        }

        foreach (var segment in TurnSegment.Split(rows))
        {
            if (segment.Lead is { } lead)
            {
                AddProseRow(lead);
            }

            if (segment.HasWork)
            {
                AddRaw(WorkFold(segment.Work));
            }

            DrawInline(segment.Tail);
        }
    }

    /// <summary>
    /// Draws rows as they are, with consecutive work gathered under one activity
    /// group.
    /// </summary>
    /// <param name="rows">The rows to draw.</param>
    private void DrawInline(IReadOnlyList<TranscriptItem> rows)
    {
        var work = new List<TranscriptItem>();
        foreach (var row in rows)
        {
            if (row is SayItem or UserItem)
            {
                Flush(work);
                AddProseRow(row);
                continue;
            }

            work.Add(row);
        }

        Flush(work);
    }

    /// <summary>Draws a user or answer row following its transcript separator.</summary>
    /// <param name="row">The transcript row.</param>
    private void AddProseRow(TranscriptItem row)
    {
        var control = RowOf(row);
        if (row.Id == _newAnswerAt)
        {
            AddRaw(new NewAnswerLine(Language));
            if (row is UserItem)
            {
                control.Margin = default;
            }
        }
        else if (row is UserItem && _drawn.Count > 0)
        {
            AddRaw(new Border { Height = 1, Background = Brush("BorderBrush"), Margin = new Thickness(0, 32, 0, 12) });
        }

        AddRow(control);
    }

    /// <summary>
    /// Draws the work gathered so far, as one group when there is any of it.
    /// </summary>
    /// <param name="work">The rows gathered; cleared as they are drawn.</param>
    private void Flush(List<TranscriptItem> work)
    {
        if (work.Count == 0)
        {
            return;
        }

        var answer = (_drawn.LastOrDefault() as Border)?.Child as AnswerCard;
        var group = ActivityGroup(work);
        if (answer is not null)
        {
            answer.SetActivity(group);
        }
        else
        {
            AddRaw(group);
        }

        work.Clear();
    }

    /// <summary>
    /// Draws one turn's intermediate work under a single line.
    /// </summary>
    /// <param name="work">The turn's intermediate rows.</param>
    /// <returns>The fold, open or closed by the reader's own choice.</returns>
    /// <remarks>
    /// The body is built whether the fold is open or not, and only its visibility
    /// changes. That is not an optimisation: rebuilding it on open would throw
    /// away the text a reader had selected, and a jump to a row inside a closed
    /// fold would find nothing to land on.
    /// </remarks>
    private Control WorkFold(IReadOnlyList<TranscriptItem> work)
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
        foreach (var row in work)
        {
            if (row.Id == _newAnswerAt)
            {
                body.Children.Add(new NewAnswerLine(Language));
            }

            var control = RowOf(row);
            _drawn.Add(control);
            body.Children.Add(control);
        }

        var group = new StackPanel { Spacing = 6 };
        group.Children.Add(head);
        group.Children.Add(body);

        return new Border
        {
            Child = group,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, _drawn.Count == 0 ? 0 : 8, 0, 0),
        };
    }

    /// <summary>Draws work under a disclosure that follows the call lifecycle.</summary>
    /// <param name="work">The canonical rows in this activity.</param>
    /// <returns>The mounted disclosure and its retained body.</returns>
    private Control ActivityGroup(IReadOnlyList<TranscriptItem> work)
    {
        var tally = ActivityTally.Of(work);
        var id = work[0].Id;
        var open = FoldChoice(TranscriptPart.Activity, id) ?? _folding.StartsOpen(
            TranscriptPart.Activity,
            live: !tally.Settled || work.Any(row => _liveWork.Contains(row.Id)),
            changed: work.OfType<ToolItem>().Any(tool => TranscriptFolding.ChangedFile(tool.Call)));
        var body = new StackPanel { Name = "ActivityBody", Spacing = 2, IsVisible = open, Margin = new Thickness(19, 2, 0, 2) };
        foreach (var row in work)
        {
            var control = RowOf(row, inActivity: true);
            _drawn.Add(control);
            body.Children.Add(control);
        }

        var disclosure = new ActivityDisclosure(
            body,
            MessageCatalog.Get("chat.count_actions", Language, ("count", Format.Count(tally.Calls, Language))),
            open,
            tally.Failed > 0 ? "ErrInkBrush" : tally.Running > 0 ? "NetBrush" : "OkInkBrush",
            value => RememberFold(TranscriptPart.Activity, id, value),
            MessageCatalog.Get("chat.activity", Language))
        {
            Tag = work.ToArray(),
        };
        disclosure.SetCountDetails(tally.Calls, ActivityDetails(tally));
        disclosure.ConfigureLive(work.OfType<ToolItem>().LastOrDefault(tool => tool.Running), tally.Running, tally.Failed > 0, Language);
        return disclosure;
    }

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
    /// <returns>The group.</returns>
    private Control DelegateGroup(IReadOnlyList<ToolItem> delegates)
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
        foreach (var delegateRow in delegates)
        {
            var control = RowOf(delegateRow);
            _drawn.Add(control);
            body.Children.Add(control);
        }

        var group = new StackPanel { Spacing = 6 };
        group.Children.Add(head);
        group.Children.Add(body);

        return new Border
        {
            Background = Brush("RaisedBrush"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, _drawn.Count == 0 ? 0 : 8, 0, 0),
            Child = group,
        };
    }

    /// <summary>
    /// Adds a decoration to the column without counting it as a transcript row.
    /// </summary>
    /// <param name="control">The decoration.</param>
    /// <remarks>
    /// A decoration is not one of the rows, so it must not shift the index a
    /// streamed chunk extends: <c>_drawn</c> is the row list, and a separator in
    /// it would make every later row's text land one row early.
    /// </remarks>
    private void AddRaw(Control control)
    {
        _rows.Children.Add(control);
    }

    /// <summary>
    /// Adds one transcript row, keeping it in the row list a streamed chunk
    /// extends.
    /// </summary>
    /// <param name="control">The row's control.</param>
    /// <remarks>
    /// A row joins <c>_drawn</c> whether it sits in the column directly or inside
    /// a group, because <c>UpdateText</c> addresses a still-arriving answer by its
    /// position among the rows. Letting a group host the control without
    /// registering it would put every later chunk one row early.
    /// </remarks>
    private void AddRow(Control control)
    {
        _drawn.Add(control);
        _rows.Children.Add(control);
    }
}
