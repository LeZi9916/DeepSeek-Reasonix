using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.State.Transcript;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Reasonix.Studio.Pages;

public sealed partial class SessionPage
{
    /// <summary>Invalidates a highlight waiting on a superseded Markdown parse.</summary>
    private long _findRequest;

    /// <summary>Cancels a pending highlight when the reader leaves search.</summary>
    public void CancelFind() => _findRequest++;

    /// <summary>Moves to a matching conversation row, revealing reasoning when it contains the match.</summary>
    /// <param name="query">Text to find.</param>
    /// <param name="ordinal">Zero-based match, wrapping in either direction.</param>
    /// <returns>The actual one-based match number and total.</returns>
    public (int Current, int Total) FindMatch(string query, int ordinal)
    {
        var request = ++_findRequest;
        if (string.IsNullOrWhiteSpace(query))
        {
            return (0, 0);
        }

        var matches = _state.Items.Select((row, index) => (row, index)).Where(pair => pair.row switch
        {
            UserItem user => Has(user.Text),
            SayItem say => Has(say.Text) || Has(say.Reasoning),
            NoticeItem notice => Has(notice.Text),
            ToolItem tool => Has(tool.Call.Name) || Has(tool.Call.Output),
            _ => false,
        }).ToArray();
        if (matches.Length == 0)
        {
            return (0, 0);
        }

        PauseTranscriptFollow();
        var selected = ((ordinal % matches.Length) + matches.Length) % matches.Length;
        var index = matches[selected].index;
        if (RevealTranscriptRow(index) is { } control)
        {
            control.BringIntoView();
            if (control is Border { Child: AnswerCard card })
            {
                card.RevealReasoning(query);
                card.WhenMarkdownReady(() => SelectMatch(control));
            }
            else
            {
                if (control is Border { Child: ToolCallHeader tool } && _state.Items[index] is ToolItem item && Has(item.Call.Output))
                {
                    tool.Reveal();
                }

                SelectMatch(control);
            }
        }

        return (selected + 1, matches.Length);

        void SelectMatch(Control row)
        {
            if (request != _findRequest)
            {
                return;
            }

            var text = row.GetSelfAndVisualDescendants().OfType<SelectableTextBlock>().FirstOrDefault(t => Has(t.Text ?? t.Inlines?.Text));
            if (text is not null)
            {
                var at = (text.Text ?? text.Inlines?.Text ?? string.Empty).IndexOf(query, StringComparison.CurrentCultureIgnoreCase);
                text.SelectionStart = at;
                text.SelectionEnd = at + query.Length;
                text.BringIntoView();
            }
        }

        bool Has(string? value) => value?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true;
    }

    /// <summary>Brings the requested run's originating tool call into the conversation viewport.</summary>
    /// <param name="id">The kernel's call identity.</param>
    public void RevealToolCall(string id)
    {
        PauseTranscriptFollow();
        for (var i = 0; i < _state.Items.Count; i++)
        {
            if (_state.Items[i] is ToolItem tool && tool.Call.ID == id)
            {
                if (RevealTranscriptRow(i) is { } row)
                {
                    Dispatcher.UIThread.Post(() => JumpToTranscriptRow(row), DispatcherPriority.Loaded);
                }

                return;
            }
        }
    }

    /// <summary>Opens any containing fold before the reader jumps to a row.</summary>
    /// <param name="index">The canonical transcript row index.</param>
    /// <returns>The row that can be brought into view.</returns>
    private Control? RevealTranscriptRow(int index)
    {
        if ((uint)index >= (uint)_state.Items.Count)
        {
            return null;
        }

        var id = _state.Items[index].Id;
        if (_locations.TryGetValue(id, out var location))
        {
            var entry = _viewportRows[location];
            if (entry.Decoration == RowKind.Work && !DisclosureChoices.WorkOpen)
            {
                DisclosureChoices.WorkOpen = true;
                Rebuild(_state.Items);
            }
            else if (entry.Decoration == RowKind.Activity && entry.Children.Count > InlineChildren)
            {
                RememberFold(TranscriptPart.Activity, entry.Children[0].Id, true);
                Rebuild(_state.Items);
            }
        }

        var control = Locate(id);
        if (control is null && _locations.TryGetValue(id, out location))
        {
            var entry = _viewportRows[location];
            var container = _rows.ContainerFromIndex(location);
            var activity = container?.GetVisualDescendants().OfType<ActivityDisclosure>()
                .FirstOrDefault(group => group.Tag is IReadOnlyList<TranscriptItem> work && work.Any(row => row.Id == id));
            if (entry.Children.Count > 0 && activity is not null)
            {
                activity.Reveal();
                control = Locate(id);
            }
        }

        if (control is not null)
        {
            foreach (var group in control.GetVisualAncestors().OfType<ActivityDisclosure>())
            {
                group.Reveal();
            }
        }

        return control;
    }
}
