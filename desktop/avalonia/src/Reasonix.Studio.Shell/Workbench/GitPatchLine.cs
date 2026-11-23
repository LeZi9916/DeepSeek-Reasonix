using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Reasonix.Studio.Shell.Workbench;

/// <summary>A displayed row in a unified Git hunk.</summary>
/// <param name="Old">The source line number, when present.</param><param name="New">The destination line number, when present.</param>
/// <param name="Kind">The unified patch marker.</param><param name="Text">The displayed code or hunk heading.</param>
internal sealed partial record GitPatchLine(string Old, string New, char Kind, string Text)
{
    /// <summary>Gets a value indicating whether the row adds a line.</summary>
    public bool IsAddition => Kind == '+';

    /// <summary>Gets a value indicating whether the row removes a line.</summary>
    public bool IsRemoval => Kind == '-';

    /// <summary>Gets a value indicating whether the row begins a hunk.</summary>
    public bool IsHeading => Kind == '@';

    /// <summary>Gets the sign in the third gutter column.</summary>
    public string Sign => IsAddition ? "+" : IsRemoval ? "-" : string.Empty;

    /// <summary>Projects hunk headers and their independently numbered lines.</summary>
    /// <param name="patch">The patch returned by the workspace host.</param>
    /// <returns>The display rows, excluding file and newline metadata.</returns>
    internal static IReadOnlyList<GitPatchLine> Read(string patch)
    {
        var rows = new List<GitPatchLine>();
        using var input = new StringReader(patch);
        long old = 0;
        long next = 0;
        var inHunk = false;
        while (input.ReadLine() is { } line)
        {
            var header = HunkHeader().Match(line);
            if (header.Success
                && long.TryParse(header.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var oldStart)
                && long.TryParse(header.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var nextStart))
            {
                old = oldStart;
                next = nextStart;
                inHunk = true;
                rows.Add(new GitPatchLine(string.Empty, string.Empty, '@', line));
                continue;
            }

            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                inHunk = false;
            }

            if (!inHunk || line.StartsWith('\\'))
            {
                continue;
            }

            var marker = line.Length == 0 ? ' ' : line[0];
            var text = line.Length == 0 ? string.Empty : line[1..];
            var source = marker == '+' ? string.Empty : (old++).ToString(CultureInfo.InvariantCulture);
            var destination = marker == '-' ? string.Empty : (next++).ToString(CultureInfo.InvariantCulture);
            rows.Add(new GitPatchLine(source, destination, marker, text));
        }

        return rows;
    }

    /// <summary>Matches a unified hunk's old and new start positions.</summary>
    /// <returns>The generated pattern.</returns>
    [GeneratedRegex(@"^@@ -([0-9]+)(?:,[0-9]+)? \+([0-9]+)(?:,[0-9]+)? @@")]
    private static partial Regex HunkHeader();
}
