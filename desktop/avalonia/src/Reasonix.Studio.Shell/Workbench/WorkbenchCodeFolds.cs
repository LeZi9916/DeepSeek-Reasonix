using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Reasonix.Studio.Shell.Workbench;

/// <summary>Finds structural multi-line folding ranges without changing the file text.</summary>
internal static partial class WorkbenchCodeFolds
{
    /// <summary>Checks whether the file has a supported folding grammar.</summary>
    /// <param name="extension">The lowercased file extension.</param><returns>Whether folding is available.</returns>
    internal static bool Supports(string extension) =>
        (extension is ".py" or ".yaml" or ".yml" or ".md" or ".markdown" or ".mdx") || IsBraced(extension);

    /// <summary>Builds ranges in the order required by AvaloniaEdit's folding manager.</summary>
    /// <param name="document">The selected editor document.</param><param name="extension">The lowercased file extension.</param>
    /// <returns>The foldable ranges, sorted by start position.</returns>
    internal static NewFolding[] Create(TextDocument document, string extension) => (extension switch
    {
        ".py" or ".yaml" or ".yml" => Indented(document),
        ".md" or ".markdown" or ".mdx" => MarkdownSections(document),
        _ => IsBraced(extension) ? Braced(document) : [],
    }).OrderBy(fold => fold.StartOffset).ToArray();

    /// <summary>Identifies languages whose multi-line blocks are delimited by braces.</summary>
    /// <param name="extension">The lowercased file extension.</param><returns>Whether braces define structural folds.</returns>
    private static bool IsBraced(string extension) => extension is ".go" or ".rs" or ".cs" or ".java" or ".c" or ".h" or ".cpp" or ".hpp"
        or ".js" or ".jsx" or ".mjs" or ".cjs" or ".ts" or ".tsx" or ".css" or ".json";

    /// <summary>Matches comments and strings as units before examining brace tokens.</summary>
    /// <returns>The lexical scanner for brace-delimited files.</returns>
    [GeneratedRegex("""//[^\r\n]*|/\*[\s\S]*?(?:\*/|\z)|"(?:\\[^\r\n]|[^"\\\r\n])*"|(?:'(?:\\[^\r\n]|[^'\\\r\n])*')|`[^`]*`|[\[\]{}]""", RegexOptions.CultureInvariant)]
    private static partial Regex Tokens();

    /// <summary>Collects matched pairs of brackets or braces spanning multiple lines.</summary>
    /// <param name="document">The selected source document.</param><returns>Its brace ranges.</returns>
    private static List<NewFolding> Braced(TextDocument document)
    {
        var folds = new List<NewFolding>();
        var groups = new Stack<(char End, int Start)>();
        foreach (var token in Tokens().EnumerateMatches(document.Text))
        {
            if (token.Length != 1)
            {
                continue;
            }

            var value = document.GetCharAt(token.Index);
            if (value is '{' or '[')
            {
                groups.Push((value == '{' ? '}' : ']', token.Index));
            }
            else if (groups.TryPeek(out var group) && group.End == value)
            {
                groups.Pop();
                if (document.GetLineByOffset(group.Start).LineNumber != document.GetLineByOffset(token.Index).LineNumber)
                {
                    folds.Add(new NewFolding(group.Start, token.Index + 1) { Name = value == '}' ? "{…}" : "[…]" });
                }
            }
        }

        return folds;
    }

    /// <summary>Collects indented blocks using the next non-blank line at or above their indentation.</summary>
    /// <param name="document">The selected source document.</param><returns>Its indentation ranges.</returns>
    private static List<NewFolding> Indented(TextDocument document)
    {
        var folds = new List<NewFolding>();
        var groups = new Stack<(int Indent, int End)>();
        var prior = (-1, 0);
        foreach (var line in document.Lines)
        {
            var text = document.GetText(line);
            var prefix = text.AsSpan().TrimStart();
            if (prefix.IsEmpty || prefix[0] == '#')
            {
                continue;
            }

            var indent = text.Length - prefix.Length;
            while (groups.TryPeek(out var group) && indent <= group.Indent)
            {
                groups.Pop();
                folds.Add(new NewFolding(group.End, prior.Item2) { Name = "…" });
            }

            if (prior.Item1 >= 0 && indent > prior.Item1)
            {
                groups.Push(prior);
            }

            prior = (indent, line.EndOffset);
        }

        foreach (var group in groups)
        {
            folds.Add(new NewFolding(group.End, prior.Item2) { Name = "…" });
        }

        return folds;
    }

    /// <summary>Closes a heading's section at its next sibling or ancestor.</summary>
    /// <param name="document">The selected Markdown source.</param><returns>Its named sections.</returns>
    private static List<NewFolding> MarkdownSections(TextDocument document)
    {
        var headings = new Stack<(int Level, int Offset)>();
        var folds = new List<NewFolding>();
        foreach (var heading in Markdig.Markdown.Parse(document.Text).OfType<Markdig.Syntax.HeadingBlock>())
        {
            var start = document.GetLineByOffset(heading.Span.Start);
            CloseSections(heading.Level, start.PreviousLine?.EndOffset ?? 0);
            var end = document.GetLineByOffset(heading.Span.End);
            headings.Push((heading.Level, end.EndOffset));
        }

        CloseSections(0, document.TextLength);
        return folds;

        void CloseSections(int level, int boundary)
        {
            while (headings.TryPeek(out var section) && section.Level >= level)
            {
                headings.Pop();
                if (boundary > section.Offset)
                {
                    folds.Add(new NewFolding(section.Offset, boundary) { Name = "…" });
                }
            }
        }
    }
}
