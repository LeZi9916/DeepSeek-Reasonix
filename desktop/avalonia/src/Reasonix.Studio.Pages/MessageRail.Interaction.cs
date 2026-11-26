using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Linq;

namespace Reasonix.Studio.Pages;

internal sealed partial class MessageRail
{
    /// <summary>How the neighboring strokes grow around the pointed segment.</summary>
    private static readonly int[] s_growth = [12, 6, 2];

    /// <summary>Finds the closest segment within the reference's forty-pixel reach.</summary>
    /// <param name="y">The pointer's height on the rail.</param>
    private void Aim(double y)
    {
        var nearest = -1;
        var distance = double.PositiveInfinity;
        for (var index = 0; index < _segments.Count; index++)
        {
            var (first, last, _, _) = _segments[index];
            var gap = Math.Max(0, Math.Max(_positions[first] - y, y - _positions[last]));
            if (gap < distance)
            {
                distance = gap;
                nearest = index;
            }
        }

        if (distance > 40)
        {
            Highlight(-1);
            return;
        }

        _closer.Stop();
        var segment = _segments[nearest];
        if (segment.First == segment.Last)
        {
            CloseRun();
        }
        else
        {
            OpenRun(nearest);
        }

        Highlight(nearest);
    }

    /// <summary>Shows the pointed message and updates nearby strokes.</summary>
    /// <param name="slot">The pointed segment, or negative when none is active.</param>
    private void Highlight(int slot)
    {
        if (_active == slot && Peek.IsVisible && _opened < 0 && slot >= 0)
        {
            return;
        }

        UpdateHighlight(slot);
        if (slot < 0 || _opened >= 0 || _segments[slot].First != _segments[slot].Last)
        {
            _peekId = null;
            Peek.Classes.Remove("shown");
            Peek.IsVisible = false;
            return;
        }

        ShowPeek(_segments[slot].First);
    }

    /// <summary>Moves the current highlight without replacing the preview.</summary>
    /// <param name="slot">The selected marker, or negative when none is active.</param>
    private void UpdateHighlight(int slot)
    {
        _active = slot;
        for (var index = 0; index < _segments.Count; index++)
        {
            var (first, last, button, line) = _segments[index];
            button.Classes.Set("current", index == slot);
            if (first == last)
            {
                var distance = Math.Abs(index - slot);
                line.Width = 10 + (slot >= 0 && distance < s_growth.Length ? s_growth[distance] : 0);
            }
        }
    }

    /// <summary>Anchors the preview to the current location of the same message.</summary>
    /// <param name="mark">The selected message index.</param>
    private void ShowPeek(int mark)
    {
        _peekId = _marks[mark].Id;
        Heading.Text = MessageCatalog.Get("chat.your_message_n", _language, ("n", Number(mark + 1)));
        Excerpt.Text = _marks[mark].Text;
        Peek.IsVisible = true;
        PlaceOverlay(Peek, _positions[mark], 44);
        Peek.Classes.Add("shown");
    }

    /// <summary>Shows the individual questions belonging to a clustered segment.</summary>
    /// <param name="slot">The cluster to open.</param>
    private void OpenRun(int slot)
    {
        if (_opened == slot)
        {
            return;
        }

        _opened = slot;
        var (first, last, _, _) = _segments[slot];
        RunRows.Children.Clear();
        RunRows.Children.Add(new TextBlock
        {
            Text = MessageCatalog.Get("chat.questions_a_to_b", _language, ("a", Number(first + 1)), ("b", Number(last + 1))),
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            [!TextBlock.FontSizeProperty] = StudioTypography.Size(12),
            [!TextBlock.ForegroundProperty] = StudioTypography.Resource("MutedBrush"),
            Margin = new Thickness(8, 4, 8, 6),
        });
        for (var mark = first; mark <= last; mark++)
        {
            var message = _marks[mark];
            var text = message.Text;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("22,*"), ColumnSpacing = 8 };
            var number = new TextBlock
            {
                Text = Number(mark + 1),
                [!TextBlock.FontSizeProperty] = StudioTypography.Size(12),
                [!TextBlock.ForegroundProperty] = StudioTypography.Resource("FaintBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var excerpt = new TextBlock
            {
                Text = text[..Math.Min(60, text.Length)],
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                [!TextBlock.FontSizeProperty] = StudioTypography.Size(12),
            };
            Grid.SetColumn(excerpt, 1);
            row.Children.Add(number);
            row.Children.Add(excerpt);
            var button = new Button { Content = row };
            button.Classes.Add("message-run-row");
            button.Click += (_, _) =>
            {
                CloseRun();
                var index = Array.FindIndex(_marks, item => item.Id == message.Id);
                if (index >= 0)
                {
                    Jump(index);
                }
            };
            RunRows.Children.Add(button);
        }

        Run.IsVisible = true;
        PlaceOverlay(Run, (_positions[first] + _positions[last]) / 2, 150);
        Peek.Classes.Remove("shown");
        Peek.IsVisible = false;
    }

    /// <summary>Closes a cluster list and clears its close timer.</summary>
    private void CloseRun()
    {
        _closer.Stop();
        _opened = -1;
        Run.IsVisible = false;
    }

    /// <summary>Anchors a preview to the track and clamps it away from the viewport edges.</summary>
    /// <param name="overlay">The preview or cluster list.</param>
    /// <param name="y">The marker position.</param>
    /// <param name="edge">The reserved distance from the nearest edge.</param>
    private void PlaceOverlay(Border overlay, double y, double edge)
    {
        overlay.Margin = default;
        overlay.Measure(new Size(250, double.PositiveInfinity));
        var center = Math.Clamp(y, edge, Math.Max(edge, Bounds.Height - edge));
        overlay.Margin = new Thickness(0, center - (overlay.DesiredSize.Height / 2), 46, 0);
    }

    /// <summary>Finds the closest individual question within a segment.</summary>
    /// <param name="slot">The pointed segment.</param>
    /// <param name="y">The pointer height.</param>
    /// <returns>The index of the question.</returns>
    private int NearestMark(int slot, double y)
    {
        var (first, last, _, _) = _segments[slot];
        var best = first;
        for (var mark = first + 1; mark <= last; mark++)
        {
            if (Math.Abs(_positions[mark] - y) < Math.Abs(_positions[best] - y))
            {
                best = mark;
            }
        }

        return best;
    }

    /// <summary>Jumps to the actual mounted row of a rail mark.</summary>
    /// <param name="index">The chosen mark.</param>
    private void Jump(int index) => JumpRequested?.Invoke(_marks[index].Id);

    /// <summary>Toggles the star of a question whose log position is known.</summary>
    /// <param name="index">The chosen mark.</param>
    private void Star(int index)
    {
        if (_marks[index].MsgIndex is not int position)
        {
            return;
        }

        if (!_stars.TryGetValue(_scope, out var stars))
        {
            _stars[_scope] = stars = [];
        }

        if (!stars.Add(position))
        {
            stars.Remove(position);
        }

        PublishBookmarks();
        for (var slot = 0; slot < _segments.Count; slot++)
        {
            RefreshStar(slot);
        }
    }

    /// <summary>Reflects stars on a single marker or its enclosing cluster.</summary>
    /// <param name="slot">The segment to update.</param>
    private void RefreshStar(int slot)
    {
        var (first, last, button, _) = _segments[slot];
        var starred = _stars.TryGetValue(_scope, out var stars)
            && Enumerable.Range(first, last - first + 1).Any(index => _marks[index].MsgIndex is int position && stars.Contains(position));
        button.Classes.Set("starred", starred);
    }
}
