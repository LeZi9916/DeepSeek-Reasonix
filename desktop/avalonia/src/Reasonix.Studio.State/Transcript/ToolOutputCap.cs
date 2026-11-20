using Reasonix.Studio.Contracts.Wire;
using System;
using System.Collections.Generic;

namespace Reasonix.Studio.State.Transcript;

/// <summary>
/// What the page keeps of a finished call's output, and what it let go of.
/// </summary>
/// <param name="Output">The call with its output replaced by its two ends, when it was long.</param>
/// <param name="Hidden">How many characters of the middle were let go of; 0 when nothing was.</param>
public readonly record struct CappedOutput(Tool Output, int Hidden);

/// <summary>
/// Bounds how much of a finished call's output the page holds.
/// </summary>
/// <remarks>
/// Ported from the reference frontend's <c>state/outputcap.ts</c>. The kernel
/// already bounds a result for the model; this bounds what a window holds and
/// draws. A call that printed ten megabytes keeps its first and last four
/// kilobytes, and the count of what was dropped is carried so the card can say
/// so and ask the kernel for the rest a page at a time.
/// </remarks>
public static class ToolOutputCap
{
    /// <summary>
    /// The longest output kept whole.
    /// </summary>
    public const int Ceiling = 16 * 1024;

    /// <summary>
    /// How much of each end is kept.
    /// </summary>
    public const int Edge = 4 * 1024;

    /// <summary>
    /// The mark drawn between the two ends.
    /// </summary>
    public const string Ellipsis = "\n⋯\n";

    /// <summary>
    /// The calls whose whole output something else reads: a plan, a remembered
    /// fact, a diff drawn as a diff, and the lookups whose row and hit counts are
    /// counted from the text.
    /// </summary>
    /// <remarks>
    /// Cutting one of these would change what that other reader counts, so the
    /// output is kept whole even when it is long.
    /// </remarks>
    private static readonly HashSet<string> s_whole = new HashSet<string>(StringComparer.Ordinal)
    {
        "todo_write",
        "remember",
        "update_goal",
        "read_file",
        "grep",
        "glob",
        "ls",
    };

    /// <summary>
    /// Reads what the page should keep of one call's output.
    /// </summary>
    /// <param name="tool">The finished call.</param>
    /// <returns>The call as the page holds it, and how many characters were dropped.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="tool"/> is null.</exception>
    public static CappedOutput Apply(Tool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        var output = tool.Output ?? string.Empty;
        if (output.Length <= Ceiling || tool.OutputDiff || tool.Diff is not null || s_whole.Contains(tool.Name))
        {
            return new CappedOutput(tool, 0);
        }

        var head = Edge;
        var tail = output.Length - Edge;

        // Cutting between the halves of a surrogate pair would leave half a
        // character at an end, which no font can draw.
        if (IsHighSurrogate(output[head - 1]))
        {
            head -= 1;
        }

        if (IsLowSurrogate(output[tail]))
        {
            tail += 1;
        }

        var kept = string.Concat(output.AsSpan(0, head), Ellipsis, output.AsSpan(tail));
        var trimmed = tool with
        {
            Output = kept,
        };

        return new CappedOutput(trimmed, tail - head);
    }

    /// <summary>
    /// Reports whether a character begins a surrogate pair.
    /// </summary>
    /// <param name="value">The character to read.</param>
    /// <returns>True for a high surrogate.</returns>
    private static bool IsHighSurrogate(char value)
    {
        return value >= 0xd800 && value <= 0xdbff;
    }

    /// <summary>
    /// Reports whether a character ends a surrogate pair.
    /// </summary>
    /// <param name="value">The character to read.</param>
    /// <returns>True for a low surrogate.</returns>
    private static bool IsLowSurrogate(char value)
    {
        return value >= 0xdc00 && value <= 0xdfff;
    }
}
