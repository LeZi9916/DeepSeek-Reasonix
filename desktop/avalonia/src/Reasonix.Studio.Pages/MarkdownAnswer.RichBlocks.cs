using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;
using CSharpMath.Avalonia;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Reasonix.Studio.Controls;
using System;
using System.Linq;

namespace Reasonix.Studio.Pages;

public static partial class MarkdownAnswer
{
    /// <summary>Appends a formula with a readable accessible name.</summary>
    /// <param name="runs">Destination inlines.</param><param name="formula">The source LaTeX.</param>
    private static void Formula(InlineCollection runs, string formula)
    {
        var math = new MathView { LaTeX = formula, MaxWidth = 760 };
        math.Bind(MathView.TextColorProperty, StudioTypography.Resource("Text"));
        AutomationProperties.SetName(math, formula);
        ToolTip.SetTip(math, formula);
        runs.Add(new InlineUIContainer { Child = math, BaselineAlignment = BaselineAlignment.Center });
    }

    /// <summary>Presents a pipe table as selectable cells in a horizontal viewport.</summary>
    /// <param name="table">The parsed Markdown table.</param><param name="runs">Destination inlines.</param>
    /// <param name="assets">Optional workspace link and image access.</param>
    private static void TableBlock(Table table, InlineCollection runs, MarkdownAssets? assets)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(',', Enumerable.Repeat("Auto", table.ColumnDefinitions.Count))) };
        var rowIndex = 0;
        foreach (var row in table.OfType<TableRow>())
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var column = 0;
            foreach (var cell in row.OfType<TableCell>())
            {
                var content = new SelectableTextBlock
                {
                    FontWeight = row.IsHeader ? FontWeight.SemiBold : FontWeight.Normal,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 360,
                    Inlines = new InlineCollection(),
                };
                Blocks(cell, content.Inlines, assets);
                var border = new Border { Child = content, Padding = new Thickness(8, 5), BorderThickness = new Thickness(0, 0, 1, 1) };
                border.Bind(Border.BorderBrushProperty, StudioTypography.Resource("HairBrush"));
                if (row.IsHeader)
                {
                    border.Bind(Border.BackgroundProperty, StudioTypography.Resource("OverlayBrush"));
                }

                Grid.SetColumn(border, column++);
                Grid.SetRow(border, rowIndex);
                grid.Children.Add(border);
            }

            rowIndex++;
        }

        var viewport = new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxWidth = 760 };
        AutomationProperties.SetAutomationId(viewport, "class:markdown-table");
        runs.Add(new InlineUIContainer { Child = viewport });
    }

    /// <summary>Uses the editor's language definitions for Markdown fences.</summary>
    /// <param name="code">The code block.</param><param name="runs">The destination inlines.</param>
    private static void CodeBlockRuns(CodeBlock code, InlineCollection runs)
    {
        var source = code.Lines.ToString();
        var syntax = code is FencedCodeBlock fence && !string.IsNullOrEmpty(fence.Info) ? Highlighting(fence.Info) : null;
        var span = new Span { [!TextElement.FontFamilyProperty] = StudioTypography.Resource("StudioMonoFamily") };
        runs.Add(span);
        if (syntax is null || source.Length > 100_000)
        {
            span.Inlines.Add(new Run(source));
            return;
        }

        var document = new TextDocument(source);
        using var highlighter = new DocumentHighlighter(document, syntax);
        foreach (var line in document.Lines)
        {
            var consumed = line.Offset;
            foreach (var section in highlighter.HighlightLine(line.LineNumber).Sections)
            {
                var start = Math.Max(consumed, section.Offset);
                var end = Math.Min(line.EndOffset, section.Offset + section.Length);
                if (start >= end)
                {
                    continue;
                }

                if (consumed < start)
                {
                    span.Inlines.Add(new Run(source[consumed..start]));
                }

                var brush = section.Color.Name?.ToLowerInvariant() switch
                {
                    "comment" or "doccomment" => "MutedBrush",
                    "string" or "char" => "SynStrBrush",
                    "number" or "numericliteral" => "SynNumBrush",
                    "methodcall" or "function" => "SynFnBrush",
                    _ => "SynKeyBrush",
                };
                span.Inlines.Add(new Run(source[start..end]) { [!TextElement.ForegroundProperty] = StudioTypography.Resource(brush) });
                consumed = end;
            }

            var next = line.Offset + line.TotalLength;
            if (consumed < next)
            {
                span.Inlines.Add(new Run(source[consumed..next]));
            }
        }
    }

    /// <summary>Maps common fence names to the editor's registered definitions.</summary>
    /// <param name="name">Declared fence language.</param><returns>The available definition.</returns>
    private static IHighlightingDefinition? Highlighting(string name)
    {
        var extension = name.ToLowerInvariant() switch
        {
            "csharp" => "cs",
            "javascript" => "js",
            "typescript" => "ts",
            "python" => "py",
            "shell" or "bash" => "sh",
            "markdown" => "md",
            _ => name.ToLowerInvariant(),
        };
        return HighlightingManager.Instance.GetDefinitionByExtension("." + extension);
    }
}
