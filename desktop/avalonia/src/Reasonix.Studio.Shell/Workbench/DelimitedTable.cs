using System;
using System.Collections.Generic;
using System.Text;

namespace Reasonix.Studio.Shell.Workbench;

/// <summary>Parses bounded comma- or tab-delimited previews.</summary>
internal static class DelimitedTable
{
    /// <summary>Reads cells, preserving quoted fields, leading zeroes and blank rows.</summary>
    /// <param name="text">The versioned document text.</param>
    /// <param name="delimiter">The file's field separator.</param>
    /// <returns>The parsed rows.</returns>
    internal static IReadOnlyList<string[]> Parse(string text, char delimiter)
    {
        const int MaxCells = 100_000;
        var rows = new List<string[]>();
        var fields = new List<string>();
        var cursor = text.StartsWith('\uFEFF') ? 1 : 0;
        var cells = 0;
        if (cursor == text.Length)
        {
            return rows;
        }

        while (true)
        {
            if (++cells > MaxCells)
            {
                throw new FormatException();
            }

            fields.Add(ReadCell());
            if (cursor < text.Length && text[cursor] == delimiter)
            {
                cursor++;
                continue;
            }

            rows.Add(fields.ToArray());
            fields.Clear();
            if (cursor == text.Length)
            {
                return rows;
            }

            if (text[cursor++] == '\r' && cursor < text.Length && text[cursor] == '\n')
            {
                cursor++;
            }
        }

        string ReadCell()
        {
            if (cursor >= text.Length || text[cursor] != '"')
            {
                var start = cursor;
                while (cursor < text.Length && text[cursor] != delimiter && text[cursor] is not ('\r' or '\n'))
                {
                    cursor++;
                }

                return text[start..cursor];
            }

            cursor++;
            var value = new StringBuilder();
            while (cursor < text.Length)
            {
                var ch = text[cursor++];
                if (ch != '"')
                {
                    value.Append(ch);
                    continue;
                }

                if (cursor < text.Length && text[cursor] == '"')
                {
                    cursor++;
                    value.Append('"');
                    continue;
                }

                while (cursor < text.Length && text[cursor] != delimiter && text[cursor] is (' ' or '\t'))
                {
                    cursor++;
                }

                if (cursor == text.Length || text[cursor] == delimiter || text[cursor] is '\r' or '\n')
                {
                    return value.ToString();
                }

                break;
            }

            throw new FormatException();
        }
    }
}
