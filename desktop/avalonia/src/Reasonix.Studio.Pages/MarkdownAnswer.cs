using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Reasonix.Studio.Controls;
using System;
using System.Globalization;
using AvInline = Avalonia.Controls.Documents.Inline;

namespace Reasonix.Studio.Pages;

/// <summary>Projects CommonMark into selectable native text and explicitly activated links.</summary>
public static class MarkdownAnswer
{
    /// <summary>Raw HTML is never an executable presentation surface.</summary>
    private static readonly MarkdownPipeline s_pipeline = new MarkdownPipelineBuilder().DisableHtml().Build();

    /// <summary>Updates a mounted answer in place.</summary>
    /// <param name="text">The text control that owns the selection.</param>
    /// <param name="source">Markdown from the conversation.</param>
    public static void Show(SelectableTextBlock text, string source)
    {
        var start = text.SelectionStart;
        var end = text.SelectionEnd;
        var document = Markdown.Parse(source, s_pipeline);
        if (document.Count == 0 || (document.Count == 1 && document[0] is ParagraphBlock { Inline.FirstChild: LiteralInline literal }
            && literal.NextSibling is null && literal.Content.ToString() == source))
        {
            text.Text = source;
        }
        else
        {
            var runs = new InlineCollection();
            Blocks(document, runs);
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
    private static void Blocks(ContainerBlock blocks, InlineCollection runs)
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

            switch (block)
            {
                case HeadingBlock heading:
                    var title = new Bold { FontSize = Math.Max(15, 24 - (heading.Level * 2)) };
                    Inlines(heading.Inline, title.Inlines);
                    runs.Add(title);
                    break;
                case CodeBlock code:
                    runs.Add(new Run(code.Lines.ToString()) { [!TextBlock.FontFamilyProperty] = StudioTypography.Resource("StudioMonoFamily") });
                    break;
                case QuoteBlock quote:
                    runs.Add(new Run("│  "));
                    Blocks(quote, runs);
                    break;
                case ContainerBlock nested:
                    Blocks(nested, runs);
                    break;
                case LeafBlock leaf:
                    Inlines(leaf.Inline, runs);
                    break;
            }
        }
    }

    /// <summary>Converts text, emphasis, inline code and links without loading external resources.</summary>
    /// <param name="container">Parsed inline content.</param>
    /// <param name="runs">The destination native runs.</param>
    private static void Inlines(ContainerInline? container, InlineCollection runs)
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
                    Inlines(emphasis, styled.Inlines);
                    runs.Add(styled);
                    break;
                case LinkInline link:
                    var label = new Span();
                    Inlines(link, label.Inlines);
                    if (!link.IsImage && Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
                    {
                        var button = new HyperlinkButton
                        {
                            Content = label.Inlines.Text ?? uri.AbsoluteUri,
                            NavigateUri = uri,
                            Padding = new Thickness(0),
                            MinHeight = 0,
                        };
                        ToolTip.SetTip(button, uri.AbsoluteUri);
                        runs.Add(new InlineUIContainer { Child = button });
                    }
                    else
                    {
                        runs.Add(label);
                    }

                    break;
                case ContainerInline nested:
                    Inlines(nested, runs);
                    break;
            }
        }
    }
}
