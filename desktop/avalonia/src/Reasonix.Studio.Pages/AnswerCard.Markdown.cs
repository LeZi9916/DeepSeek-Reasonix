using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Markdig.Syntax;
using Reasonix.Studio.Controls;
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Reasonix.Studio.Pages;

internal sealed partial class AnswerCard
{
    /// <summary>Offsets of the last painted content, keyed weakly to each native block.</summary>
    private readonly ConditionalWeakTable<SelectableTextBlock, BlockPaint> _blockPaint = new();

    /// <summary>Source last applied to the native blocks.</summary>
    private string _paintedSource = string.Empty;

    /// <summary>Whether an active parse loop owns the newest pending source.</summary>
    private bool _parsingMarkdown;

    /// <summary>Changes whenever the card leaves its current mount.</summary>
    private long _markdownMount;

    /// <summary>Applies the most recent deferred selection after the answer is ready.</summary>
    private Action? _markdownSelection;

    /// <summary>Registers a selection action for the current answer.</summary>
    /// <param name="select">The selection to perform against native text.</param>
    internal void WhenMarkdownReady(Action select)
    {
        if (_paintedSource == _source)
        {
            select();
        }
        else
        {
            _markdownSelection = select;
        }
    }

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
    /// <param name="document">The parsed Markdown.</param>
    /// <returns>The last visible text block in the parsed Markdown.</returns>
    private SelectableTextBlock ShowMarkdown(string source, MarkdownDocument document)
    {
        var blocks = this.FindControl<StackPanel>("AnswerBlocks")!;
        var canReuse = !source.Contains("]:", StringComparison.Ordinal) && !_paintedSource.Contains("]:", StringComparison.Ordinal);
        if (document.Count <= 1)
        {
            Trim(blocks, 1);
            if (document.Count == 0)
            {
                _blockPaint.Remove(_answer);
                _answer.Text = source;
            }
            else
            {
                Paint(_answer, document[0], source, canReuse);
            }

            _paintedSource = source;
            return _answer;
        }

        Paint(_answer, document[0], source, canReuse);
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
                    if (bullet.Foreground is null || bullet.Tag is null)
                    {
                        bullet.Bind(TextBlock.ForegroundProperty, StudioTypography.Resource("FaintBrush"));
                        bullet.Tag = true;
                    }

                    var content = ChildAt(row, 1, ReadingText);
                    var leaf = entry.FirstOrDefault();
                    if (leaf is not null)
                    {
                        Paint(content, entry, source, canReuse);
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
                Paint(text, block, source, canReuse);
                last = text;
            }

            blockIndex++;
        }

        Trim(blocks, blockIndex);
        _paintedSource = source;
        return last;
    }

    /// <summary>Updates a block whose content or parsed type changed.</summary>
    /// <param name="text">Native destination.</param><param name="block">Parsed block.</param>
    /// <param name="source">Canonical Markdown.</param><param name="canReuse">Whether parsing is local to the block.</param>
    private void Paint(SelectableTextBlock text, Block block, string source, bool canReuse)
    {
        var start = Math.Clamp(block.Span.Start, 0, source.Length);
        var length = Math.Clamp(block.Span.End - start + 1, 0, source.Length - start);
        var span = source.AsSpan(start, length);
        var stamp = _blockPaint.GetOrCreateValue(text);
        var kind = block.GetType();
        if (!canReuse || stamp.Kind != kind || stamp.Start > _paintedSource.Length - stamp.Length
            || !span.SequenceEqual(_paintedSource.AsSpan(stamp.Start, stamp.Length)))
        {
            MarkdownAnswer.ShowParsed(text, block, span);
        }

        stamp.Kind = kind;
        stamp.Start = start;
        stamp.Length = length;
    }

    /// <summary>Queues large parses while small blocks can be painted in one layout pass.</summary>
    private void PresentMarkdown()
    {
        if (_paintedSource == _source)
        {
            return;
        }

        if (_source.Length <= 2048)
        {
            ApplyMarkdown(_source, MarkdownAnswer.Parse(_source));
        }
        else if (!_parsingMarkdown)
        {
            _ = ParseNewestMarkdownAsync();
        }
    }

    /// <summary>Parses one snapshot at a time and publishes data only to its current mount.</summary>
    /// <returns>The asynchronous render loop.</returns>
    private async Task ParseNewestMarkdownAsync()
    {
        _parsingMarkdown = true;
        try
        {
            while (_paintedSource != _source)
            {
                var source = _source;
                var mount = _markdownMount;
                var document = await Task.Run(() => MarkdownAnswer.Parse(source)).ConfigureAwait(true);
                if (mount != _markdownMount)
                {
                    break;
                }

                if (_source == source)
                {
                    ApplyMarkdown(source, document);
                }
            }
        }
        finally
        {
            _parsingMarkdown = false;
            if (TopLevel.GetTopLevel(this) is not null && _paintedSource != _source)
            {
                PresentMarkdown();
            }
        }
    }

    /// <summary>Publishes a parsed answer and any deferred selection request.</summary>
    /// <param name="source">Current answer source.</param><param name="document">Parsed answer.</param>
    private void ApplyMarkdown(string source, MarkdownDocument document)
    {
        _answerCaret.Target = ShowMarkdown(source, document);
        var select = _markdownSelection;
        _markdownSelection = null;
        select?.Invoke();
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

    /// <summary>The previous source span painted into a selectable block.</summary>
    private sealed class BlockPaint
    {
        /// <summary>Gets or sets the parsed block type.</summary>
        internal Type? Kind
        {
            get; set;
        }

        /// <summary>Gets or sets the start of the previous source span.</summary>
        internal int Start
        {
            get; set;
        }

        /// <summary>Gets or sets the length of the previous source span.</summary>
        internal int Length
        {
            get; set;
        }
    }
}
