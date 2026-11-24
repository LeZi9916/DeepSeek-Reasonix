using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Markdig;
using Markdig.Syntax;
using Reasonix.Studio.Controls;
using System.Linq;

namespace Reasonix.Studio.Pages;

internal sealed partial class AnswerCard
{
    /// <summary>Retains selectable blocks while the parsed Markdown shape is stable.</summary>
    /// <typeparam name="T">The required native block.</typeparam>
    /// <param name="panel">Owning block container.</param>
    /// <param name="index">Position in the parsed document.</param>
    /// <param name="create">Creates a newly introduced block.</param>
    /// <returns>The mounted block.</returns>
    private static T ChildAt<T>(Panel panel, int index, System.Func<T> create)
        where T : Control
    {
        if (index < panel.Children.Count && panel.Children[index] is T held)
        {
            return held;
        }

        var child = create();
        if (index < panel.Children.Count)
        {
            panel.Children[index] = child;
        }
        else
        {
            panel.Children.Add(child);
        }

        return child;
    }

    /// <summary>Removes blocks no longer present in the canonical Markdown.</summary>
    /// <param name="panel">The block container.</param>
    /// <param name="count">Parsed block count.</param>
    private static void Trim(Panel panel, int count)
    {
        while (panel.Children.Count > count)
        {
            panel.Children.RemoveAt(panel.Children.Count - 1);
        }
    }

    /// <summary>Projects block spacing without inserting selectable empty lines into the message.</summary>
    /// <param name="source">The original Markdown.</param>
    /// <returns>The last visible text block in the parsed Markdown.</returns>
    private SelectableTextBlock ShowMarkdown(string source)
    {
        var blocks = this.FindControl<StackPanel>("AnswerBlocks")!;
        var document = Markdown.Parse(source);
        if (document.Count <= 1)
        {
            Trim(blocks, 1);
            MarkdownAnswer.Show(_answer, source);
            return _answer;
        }

        var first = document[0];
        MarkdownAnswer.Show(_answer, source[first.Span.Start..(first.Span.End + 1)]);
        var blockIndex = 1;
        var last = _answer;
        foreach (var block in document.Skip(1))
        {
            var gap = FontSizeForReading() * 0.7;
            if (block is ListBlock list)
            {
                var rows = ChildAt(blocks, blockIndex, () => new StackPanel());
                rows.Spacing = FontSizeForReading() * 0.18;
                rows.Margin = new Thickness(0, gap, 0, 0);
                var index = 0;
                foreach (var entry in list.Cast<ListItemBlock>())
                {
                    var row = ChildAt(rows, index, () => new Grid { ColumnDefinitions = new ColumnDefinitions("19.6,*") });
                    var bullet = ChildAt(row, 0, ReadingText);
                    index++;
                    bullet.Text = list.IsOrdered ? index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "." : "•";
                    bullet.Bind(TextBlock.ForegroundProperty, StudioTypography.Resource("FaintBrush"));
                    var content = ChildAt(row, 1, ReadingText);
                    var leaf = entry.FirstOrDefault();
                    if (leaf is not null)
                    {
                        MarkdownAnswer.Show(content, source[leaf.Span.Start..(entry.Span.End + 1)]);
                        last = content;
                    }

                    Grid.SetColumn(content, 1);
                }

                Trim(rows, index);
            }
            else
            {
                var text = ChildAt(blocks, blockIndex, ReadingText);
                text.Margin = new Thickness(0, gap, 0, 0);
                MarkdownAnswer.Show(text, source[block.Span.Start..(block.Span.End + 1)]);
                last = text;
            }

            blockIndex++;
        }

        Trim(blocks, blockIndex);
        return last;
    }

    /// <summary>Reads the current independent reading size.</summary>
    /// <returns>The size in DIPs.</returns>
    private double FontSizeForReading() => _answer.FontSize;

    /// <summary>Creates selectable text with the message's inherited reading preferences.</summary>
    /// <returns>The text control.</returns>
    private SelectableTextBlock ReadingText() => new()
    {
        [!TextBlock.FontSizeProperty] = StudioTypography.Resource("StudioReadingSize"),
        [!TextBlock.LineHeightProperty] = StudioTypography.Resource("StudioReadingHeight"),
        FontWeight = FontWeight.Medium,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Top,
    };

    /// <summary>Quotes the selected block or the full original Markdown when nothing is selected.</summary>
    /// <returns>The reader's selected passage.</returns>
    private string SelectedPassage() => this.FindControl<StackPanel>("AnswerBlocks")!.GetVisualDescendants()
        .OfType<SelectableTextBlock>().FirstOrDefault(text => !string.IsNullOrEmpty(text.SelectedText))?.SelectedText ?? _source;
}
