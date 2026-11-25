using System;
using System.Threading;

namespace Reasonix.Studio.State.Transcript;

/// <summary>Shares stream segments across snapshots while retaining only the requested prefix string.</summary>
internal sealed class LiveText
{
    /// <summary>Most recent segment.</summary>
    private readonly Segment _last;

    /// <summary>Total UTF-16 units.</summary>
    private readonly int _length;

    /// <summary>Materialized prefix for this snapshot.</summary>
    private string? _value;

    /// <summary>Initializes a new instance of the <see cref="LiveText"/> class.</summary>
    /// <param name="last">Tail of the shared segments.</param>
    /// <param name="length">Total UTF-16 units.</param>
    /// <param name="hasContent">Whether a segment has visible text.</param>
    private LiveText(Segment last, int length, bool hasContent)
    {
        _last = last;
        _length = length;
        HasContent = hasContent;
    }

    /// <summary>Gets a value indicating whether any segment has non-whitespace content.</summary>
    internal bool HasContent
    {
        get;
    }

    /// <summary>Gets the snapshot's complete text.</summary>
    internal string Value
    {
        get
        {
            var cached = Volatile.Read(ref _value);
            if (cached is not null)
            {
                return cached;
            }

            var value = string.Create(_length, _last, static (destination, last) =>
            {
                for (Segment? segment = last; segment is not null; segment = segment.Previous)
                {
                    var start = destination.Length - segment.Text.Length;
                    segment.Text.AsSpan().CopyTo(destination[start..]);
                    destination = destination[..start];
                }
            });
            return Interlocked.CompareExchange(ref _value, value, null) ?? value;
        }
    }

    /// <summary>Extends the segment chain, seeding it from existing text when needed.</summary>
    /// <param name="previous">The preceding stream snapshot.</param>
    /// <param name="seed">The preceding text before streaming.</param>
    /// <param name="delta">New text.</param>
    /// <returns>The new stream snapshot.</returns>
    internal static LiveText Append(LiveText? previous, string? seed, string delta)
    {
        var last = previous?._last ?? (string.IsNullOrEmpty(seed) ? null : new Segment(seed, null));
        var length = (previous?._length ?? seed?.Length ?? 0) + delta.Length;
        var hasContent = (previous?.HasContent ?? !string.IsNullOrWhiteSpace(seed)) || !string.IsNullOrWhiteSpace(delta);
        return new LiveText(new Segment(delta, last), length, hasContent);
    }

    /// <summary>Links a delta to its immutable prefix.</summary>
    /// <param name="Text">Delta text.</param><param name="Previous">Previous delta.</param>
    private sealed record Segment(string Text, Segment? Previous);
}
