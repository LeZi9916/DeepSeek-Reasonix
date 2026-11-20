using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Reasonix.Studio.State.Transcript;

/// <summary>
/// One search hit, as a lookup prints it.
/// </summary>
/// <param name="Path">The file the hit is in.</param>
/// <param name="Line">The line number, as the tool wrote it.</param>
/// <param name="Text">The line's text.</param>
/// <param name="More">The context lines that followed it.</param>
public readonly record struct SearchHit(string Path, string Line, string Text, IReadOnlyList<string> More);

/// <summary>
/// One row of a listing: a name, and the count or size beside it.
/// </summary>
/// <param name="Name">The file or directory name.</param>
/// <param name="Count">The bytes or matches the tool printed beside it, or empty.</param>
public readonly record struct ListingRow(string Name, string Count);

/// <summary>
/// Reads the shapes a lookup's output comes in.
/// </summary>
/// <remarks>
/// Ported from the reference frontend's <c>ui/cards/ToolOutput.tsx</c>. Every
/// shape here was read off real tool results: a search hit is
/// <c>path:line:text</c>, a summary is <c>path: N matches</c>, and <c>ls</c> is
/// <c>name&lt;TAB&gt;bytes</c>.
///
/// Anything that does not parse cleanly is refused rather than forced into a
/// list. A wrong list reads as authoritative; a raw block only reads as raw, and
/// that is the better failure.
/// </remarks>
public static class ToolOutputShape
{
    /// <summary>
    /// The share of lines that must fit before a shape is claimed.
    /// </summary>
    /// <remarks>
    /// Below this the output is something else that happens to share a line or
    /// two with the shape, and drawing it as a list would invent structure.
    /// </remarks>
    private const double Confident = 0.8;

    /// <summary>
    /// Matches a search hit: path, line number, text.
    /// </summary>
    private static readonly Regex s_hit =
        new Regex(@"^(.+?):(\d+):([\s\S]*)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Matches a directory line from a listing: a name ending in a slash.
    /// </summary>
    private static readonly Regex s_directory =
        new Regex(@"^(.+/)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Matches a file line from a listing: a name, a tab, a byte count.
    /// </summary>
    private static readonly Regex s_file =
        new Regex(@"^(.+)\t(\d+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Matches the note a lookup appends when its own result was cut short.
    /// </summary>
    private static readonly Regex s_trailer =
        new Regex(@"^\.\.\. \(", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Reads search hits out of a lookup's output.
    /// </summary>
    /// <param name="output">The output the tool printed.</param>
    /// <returns>
    /// The hits and the tool's own truncation note, or null when the output is
    /// not a list of hits.
    /// </returns>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="output"/> is null.</exception>
    public static (IReadOnlyList<SearchHit> Hits, string Note)? Hits(string output)
    {
        System.ArgumentNullException.ThrowIfNull(output);

        var all = Lines(output);

        // A lookup appends its own truncation and timeout notes. They are the tool
        // saying the result is partial, not stray output, so they are pulled out
        // before the shape is judged and kept to be shown.
        var note = string.Join(" ", all.FindAll(line => s_trailer.IsMatch(line)));
        var lines = all.FindAll(line => !s_trailer.IsMatch(line));
        if (lines.Count == 0)
        {
            return null;
        }

        var hits = new List<SearchHit>();
        var loose = 0;
        foreach (var line in lines)
        {
            var match = s_hit.Match(line);
            if (match.Success)
            {
                hits.Add(new SearchHit(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value.Trim(), new List<string>()));
                continue;
            }

            // A context line repeats the hit's own path, so it is stripped by that
            // literal rather than by a pattern a path holding "-12-" would fool.
            if (hits.Count > 0)
            {
                var current = hits[hits.Count - 1];
                if (line.StartsWith(current.Path, System.StringComparison.Ordinal))
                {
                    var rest = line.Substring(current.Path.Length);
                    var more = new List<string>(current.More) { StripGutter(rest) };
                    hits[hits.Count - 1] = current with
                    {
                        More = more,
                    };
                    continue;
                }
            }

            loose += 1;
        }

        return hits.Count > 0 && (double)loose / lines.Count <= 1 - Confident
            ? (hits, note)
            : null;
    }

    /// <summary>
    /// Reads a listing out of an <c>ls</c>-shaped output.
    /// </summary>
    /// <param name="output">The output the tool printed.</param>
    /// <returns>The rows, or null when the output is not a listing.</returns>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="output"/> is null.</exception>
    public static IReadOnlyList<ListingRow>? Listing(string output)
    {
        System.ArgumentNullException.ThrowIfNull(output);

        var lines = Lines(output);
        if (lines.Count == 0)
        {
            return null;
        }

        var rows = new List<ListingRow>();
        foreach (var line in lines)
        {
            var file = s_file.Match(line);
            if (file.Success)
            {
                rows.Add(new ListingRow(file.Groups[1].Value, file.Groups[2].Value));
                continue;
            }

            var directory = s_directory.Match(line);
            if (directory.Success)
            {
                rows.Add(new ListingRow(directory.Groups[1].Value, string.Empty));
            }
        }

        return (double)rows.Count / lines.Count < Confident ? null : rows;
    }

    /// <summary>
    /// Reads a shape with a fixed number of counted fields per line.
    /// </summary>
    /// <param name="output">The output the tool printed.</param>
    /// <param name="pattern">
    /// A pattern whose first group is the name and whose optional second group is
    /// the count.
    /// </param>
    /// <returns>The rows, or null when too few lines fit the pattern.</returns>
    /// <exception cref="System.ArgumentNullException">
    /// Thrown when <paramref name="output"/> or <paramref name="pattern"/> is null.
    /// </exception>
    public static IReadOnlyList<ListingRow>? Rows(string output, Regex pattern)
    {
        System.ArgumentNullException.ThrowIfNull(output);
        System.ArgumentNullException.ThrowIfNull(pattern);

        var lines = Lines(output);
        if (lines.Count == 0)
        {
            return null;
        }

        var rows = new List<ListingRow>();
        foreach (var line in lines)
        {
            var match = pattern.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var count = match.Groups.Count > 2 ? match.Groups[2].Value : string.Empty;
            rows.Add(new ListingRow(match.Groups[1].Value, count));
        }

        return (double)rows.Count / lines.Count < Confident ? null : rows;
    }

    /// <summary>
    /// Splits output into its non-blank lines, trimmed of surrounding space.
    /// </summary>    /// <param name="output">The output.</param>
    /// <returns>The lines that hold something.</returns>
    private static List<string> Lines(string output)
    {
        var lines = new List<string>();
        foreach (var line in output.Split('\n'))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                lines.Add(line.TrimEnd('\r'));
            }
        }

        return lines;
    }

    /// <summary>
    /// Strips the <c>-12-</c> or <c>:12:</c> gutter a context line carries.
    /// </summary>
    /// <param name="rest">What followed the hit's own path.</param>
    /// <returns>The context line's text.</returns>
    private static string StripGutter(string rest)
    {
        var trimmed = rest.TrimStart('-', ':');
        var digits = 0;
        while (digits < trimmed.Length && char.IsAsciiDigit(trimmed[digits]))
        {
            digits += 1;
        }

        return digits > 0 && digits < trimmed.Length && (trimmed[digits] == '-' || trimmed[digits] == ':')
            ? trimmed.Substring(digits + 1).Trim()
            : rest.Trim();
    }
}
