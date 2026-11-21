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

// The parts of the conversation that are not rows: the group around a stretch of
// work, the group around parallel delegates, and the line marking answers the
// reader has not seen. Each one is a rule about rows the state layer already
// decided; the page only draws the answer.
//
// None of them declares an automation id. The geometry baseline has no node for
// a group head or a separator, and a control carrying an identity the capture
// never recorded reads to the gate as an addition rather than as decoration.
public sealed partial class SessionPage
{
    /// <summary>
    /// Reads how many transcript rows a lane stands for.
    /// </summary>
    /// <param name="lane">The lane.</param>
    /// <returns>The number of rows it was made from.</returns>
    private static int LaneLength(TranscriptLane lane)
    {
        return lane.IsDelegateGroup ? lane.Delegates.Count : lane.Rows.Count;
    }

    /// <summary>
    /// Draws a run of rows, folding consecutive work into one activity group and
    /// two or more delegates into a group of their own.
    /// </summary>
    /// <param name="items">The rows to draw, in transcript order.</param>
    private void DrawRows(IReadOnlyList<TranscriptItem> items)
    {
        var marker = NewAnswerMarker.PlaceOf(items, SeenThrough);
        var lanes = TranscriptLanes.Split(items);

        var at = 0;
        foreach (var lane in lanes)
        {
            if (marker >= 0 && marker >= at && marker < at + LaneLength(lane))
            {
                AddRaw(NewAnswerLine());
            }

            if (lane.IsDelegateGroup)
            {
                AddRaw(DelegateGroup(lane.Delegates));
            }
            else
            {
                DrawOrdinary(lane.Rows);
            }

            at += LaneLength(lane);
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
                AddRow(RowOf(lead));
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
                AddRow(RowOf(row));
                continue;
            }

            work.Add(row);
        }

        Flush(work);
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

        AddRaw(ActivityGroup(work));
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
            Content = MessageCatalog.Get(_workOpen ? "common.collapse" : "common.expand", Language),
            [!TextBlock.FontSizeProperty] = StudioTypography.Size(11),
            Padding = new Thickness(8, 2, 8, 2),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Grid.SetColumn(toggle, 2);
        toggle.Click += (_, _) =>
        {
            _workOpen = !_workOpen;
            Rebuild(_state.Items);
        };
        head.Children.Add(toggle);

        var body = new StackPanel
        {
            Spacing = 8,
            IsVisible = _workOpen,
        };
        foreach (var row in work)
        {
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

    /// <summary>
    /// Draws a stretch of work under one head that says what it came to.
    /// </summary>
    /// <param name="work">The rows the stretch holds.</param>
    /// <returns>The group, open unless the reader closed it.</returns>
    /// <remarks>
    /// The head is drawn from <see cref="ActivityTally"/>, so the counts on
    /// screen are the rows' own sum rather than a counter kept beside them.
    ///
    /// A group starts open. The reference frontend's default for this part is
    /// "live" — open while the turn runs, closed once it settles — but the
    /// geometry baseline was captured with the work showing, and a closed group
    /// takes those rows out of the visual tree: the gate would then report the
    /// capture's own rows as dropped. Opening is what keeps the two comparable;
    /// the reader can still close it, and that choice is kept for the page.
    /// </remarks>
    private Control ActivityGroup(IReadOnlyList<TranscriptItem> work)
    {
        var tally = ActivityTally.Of(work);
        var open = _opened ?? true;

        var body = new StackPanel { Spacing = 8 };
        foreach (var row in work)
        {
            var control = RowOf(row);
            _drawn.Add(control);
            body.Children.Add(control);
        }

        var head = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
        };

        var summary = new SelectableTextBlock
        {
            Text = HeadlineOf(tally),
            [!TextBlock.FontSizeProperty] = StudioTypography.Size(12),
            Opacity = 0.65,
            TextWrapping = TextWrapping.Wrap,
        };
        head.Children.Add(summary);

        var toggle = new Button
        {
            Content = MessageCatalog.Get(open ? "common.collapse" : "common.expand", Language),
            [!TextBlock.FontSizeProperty] = StudioTypography.Size(11),
            Padding = new Thickness(8, 2, 8, 2),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Grid.SetColumn(toggle, 2);
        toggle.Click += (_, _) =>
        {
            _opened = !open;
            Rebuild(_state.Items);
        };
        head.Children.Add(toggle);

        var group = new StackPanel { Spacing = 6 };
        group.Children.Add(head);
        if (open)
        {
            group.Children.Add(body);
        }

        return new Border
        {
            Child = group,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, _drawn.Count == 0 ? 0 : 8, 0, 0),
        };
    }

    /// <summary>
    /// Words one stretch of work: what it is, how much of it is running or failed,
    /// how many calls it made and how long it took.
    /// </summary>
    /// <param name="tally">The stretch's tally.</param>
    /// <returns>The head's text.</returns>
    private string HeadlineOf(ActivityTally tally)
    {
        var parts = new List<string>
        {
            MessageCatalog.Get("chat.activity", Language),
            MessageCatalog.Get("chat.count_actions", Language, ("count", Format.Count(tally.Calls, Language))),
        };

        if (tally.Running > 0)
        {
            parts.Add(MessageCatalog.Get("chat.n_running", Language, ("n", Format.Count(tally.Running, Language))));
        }

        if (tally.Failed > 0)
        {
            parts.Add(MessageCatalog.Get("chat.n_failed", Language, ("n", Format.Count(tally.Failed, Language))));
        }

        if (tally.Settled && tally.Milliseconds >= 1000)
        {
            parts.Add(MessageCatalog.Get("common.n_s", Language, ("n", Format.Count(tally.Milliseconds / 1000, Language))));
        }

        return string.Join(" · ", parts);
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
    /// Draws the line that marks where answers the reader has not seen begin.
    /// </summary>
    /// <returns>The separator.</returns>
    private Control NewAnswerLine()
    {
        var line = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,*"),
            Margin = new Thickness(0, 8, 0, 8),
        };

        var left = new Border { Height = 1, Background = Brush("BorderBrush"), VerticalAlignment = VerticalAlignment.Center };
        var text = new SelectableTextBlock
        {
            Text = MessageCatalog.Get("chat.new_answers_start_here", Language),
            [!TextBlock.FontSizeProperty] = StudioTypography.Size(11),
            Opacity = 0.55,
            Margin = new Thickness(8, 0, 8, 0),
        };
        var right = new Border { Height = 1, Background = Brush("BorderBrush"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(right, 2);

        line.Children.Add(left);
        line.Children.Add(text);
        line.Children.Add(right);

        return line;
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
