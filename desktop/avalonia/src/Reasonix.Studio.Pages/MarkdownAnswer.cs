using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Markdig;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Reasonix.Studio.Controls;
using System;
using System.Globalization;
using AvInline = Avalonia.Controls.Documents.Inline;

namespace Reasonix.Studio.Pages;

/// <summary>Projects CommonMark into selectable native text and explicitly activated links.</summary>
public static partial class MarkdownAnswer
{
    /// <summary>Raw HTML is never an executable presentation surface.</summary>
    private static readonly MarkdownPipeline s_pipeline = new MarkdownPipelineBuilder().UsePipeTables().UseMathematics().UseAutoLinks().DisableHtml().Build();

    /// <summary>Updates a mounted answer in place.</summary>
    /// <param name="text">The text control that owns the selection.</param>
    /// <param name="source">Markdown from the conversation.</param>
    public static void Show(SelectableTextBlock text, string source)
    {
        var document = Parse(source);
        if (document.Count == 0)
        {
            text.Text = source;
            return;
        }

        ShowParsed(text, document, source.AsSpan());
    }

    /// <summary>Parses Markdown independently of any Avalonia control.</summary>
    /// <param name="source">Markdown from the conversation.</param>
    /// <returns>The document used for block and inline rendering.</returns>
    internal static MarkdownDocument Parse(string source) => Markdown.Parse(source, s_pipeline);

    /// <summary>Paints a parsed block while preserving the selected range.</summary>
    /// <param name="text">Selectable destination.</param><param name="block">Parsed block.</param>
    /// <param name="source">Original block text.</param>
    /// <param name="assets">The owning workspace capabilities.</param>
    internal static void ShowParsed(SelectableTextBlock text, Block block, ReadOnlySpan<char> source, MarkdownAssets? assets = null)
    {
        var start = text.SelectionStart;
        var end = text.SelectionEnd;
        var paragraph = block is MarkdownDocument { Count: 1 } document ? document[0] : block;
        if (paragraph is ParagraphBlock { Inline.FirstChild: LiteralInline literal }
            && literal.NextSibling is null && literal.Content.AsSpan().SequenceEqual(source))
        {
            text.Text = source.ToString();
        }
        else
        {
            var runs = new InlineCollection();
            BlockRuns(block, runs, assets);
            text.Inlines = runs;
        }

        if (start != end)
        {
            var length = text.Inlines?.Text?.Length ?? text.Text?.Length ?? 0;
            text.SelectionStart = Math.Min(start, length);
            text.SelectionEnd = Math.Min(end, length);
        }
    }

    /// <summary>Appends paragraphs, headings, quotes, lists and code in document order.</summary>
    /// <param name="blocks">The parsed block container.</param>
    /// <param name="runs">The destination native runs.</param>
    /// <param name="assets">The owning workspace capabilities.</param>
    private static void Blocks(ContainerBlock blocks, InlineCollection runs, MarkdownAssets? assets)
    {
        var index = 0;
        foreach (var block in blocks)
        {
            if (index++ > 0)
            {
                runs.Add(new LineBreak());
                if (blocks is not ListBlock)
                {
                    runs.Add(new LineBreak());
                }
            }

            if (blocks is ListBlock list)
            {
                _ = int.TryParse(list.OrderedStart, CultureInfo.InvariantCulture, out var first);
                runs.Add(new Run(list.IsOrdered ? (first + index - 1).ToString(CultureInfo.InvariantCulture) + ". " : "•  "));
            }

            BlockRuns(block, runs, assets);
        }
    }

    /// <summary>Writes one block without rebuilding a document around it.</summary>
    /// <param name="block">Parsed block.</param><param name="runs">Native destination.</param>
    /// <param name="assets">The owning workspace capabilities.</param>
    private static void BlockRuns(Block block, InlineCollection runs, MarkdownAssets? assets)
    {
        switch (block)
        {
            case HeadingBlock heading:
                var title = new Bold { FontSize = Math.Max(15, 24 - (heading.Level * 2)) };
                Inlines(heading.Inline, title.Inlines, assets);
                runs.Add(title);
                break;
            case Table table:
                TableBlock(table, runs, assets);
                break;
            case MathBlock math:
                Formula(runs, math.Lines.ToString());
                break;
            case CodeBlock code:
                CodeBlockRuns(code, runs);
                break;
            case QuoteBlock quote:
                runs.Add(new Run("│  "));
                Blocks(quote, runs, assets);
                break;
            case ContainerBlock nested:
                Blocks(nested, runs, assets);
                break;
            case LeafBlock leaf:
                Inlines(leaf.Inline, runs, assets);
                break;
        }
    }

    /// <summary>Creates a link with decorations owned by the current UI dispatcher.</summary>
    /// <param name="content">The selectable label.</param><returns>The link control.</returns>
    private static HyperlinkButton LinkButton(string content) => new()
    {
        Content = content,
        Padding = new Thickness(0),
        MinHeight = 0,
        [TextBlock.TextDecorationsProperty] = new TextDecorationCollection
        {
            new TextDecoration { Location = TextDecorationLocation.Underline },
        },
    };

    /// <summary>Converts text, emphasis, inline code and links without loading external resources.</summary>
    /// <param name="container">Parsed inline content.</param>
    /// <param name="runs">The destination native runs.</param>
    /// <param name="assets">The owning workspace capabilities.</param>
    private static void Inlines(ContainerInline? container, InlineCollection runs, MarkdownAssets? assets)
    {
        for (var child = container?.FirstChild; child is not null; child = child.NextSibling)
        {
            switch (child)
            {
                case LiteralInline literal:
                    runs.Add(new Run(literal.Content.ToString()));
                    break;
                case HtmlEntityInline entity:
                    runs.Add(new Run(entity.Transcoded.ToString()));
                    break;
                case CodeInline code:
                    runs.Add(new Run(code.Content) { [!TextBlock.FontFamilyProperty] = StudioTypography.Resource("StudioMonoFamily") });
                    break;
                case LineBreakInline line:
                    runs.Add(line.IsHard ? new LineBreak() : (AvInline)new Run(" "));
                    break;
                case EmphasisInline emphasis:
                    Span styled = emphasis.DelimiterCount >= 2 ? new Bold() : new Italic();
                    Inlines(emphasis, styled.Inlines, assets);
                    runs.Add(styled);
                    break;
                case MathInline math:
                    Formula(runs, math.Content.ToString());
                    break;
                case LinkInline link:
                    var label = new Span();
                    Inlines(link, label.Inlines, assets);
                    if (assets is not null && link.Url is { Length: > 0 } destination && assets.Resolve(destination) is { } path)
                    {
                        if (link.IsImage)
                        {
                            runs.Add(new InlineUIContainer { Child = new MarkdownImage(path, label.Inlines.Text, assets) });
                        }
                        else
                        {
                            var file = LinkButton(label.Inlines.Text ?? path);
                            file.Click += async (_, _) => await assets.Open(path).ConfigureAwait(true);
                            ToolTip.SetTip(file, path);
                            runs.Add(new InlineUIContainer { Child = file });
                        }
                    }
                    else if (!link.IsImage && Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
                    {
                        var button = LinkButton(label.Inlines.Text ?? uri.AbsoluteUri);
                        button.NavigateUri = uri;
                        ToolTip.SetTip(button, uri.AbsoluteUri);
                        runs.Add(new InlineUIContainer { Child = button });
                    }
                    else
                    {
                        runs.Add(label);
                    }

                    break;
                case ContainerInline nested:
                    Inlines(nested, runs, assets);
                    break;
            }
        }
    }
}
