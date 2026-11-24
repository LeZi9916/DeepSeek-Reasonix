using Reasonix.Studio.State.Transcript;
using System.Collections.Generic;

namespace Reasonix.Studio.State;

/// <summary>
/// Folds streamed text onto the message being written.
/// </summary>
/// <remarks>
/// Which row a chunk belongs to is decided by walking back from the end: the
/// nearest open message, or the user row that closed the last one. Without that
/// walk a second answer would append to the first, and a chunk arriving after the
/// person spoke would land above their message.
/// </remarks>
public sealed class TextAppender
{
    /// <summary>
    /// The sequence every new row's identity is minted from.
    /// </summary>
    private readonly IdSequence _ids;

    /// <summary>
    /// Reads the current time, so a case can drive the thought clock.
    /// </summary>
    private readonly System.Func<long> _clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextAppender"/> class.
    /// </summary>
    /// <param name="ids">The sequence every new row's identity is minted from.</param>
    /// <param name="clock">Reads the current time in unix milliseconds.</param>
    /// <exception cref="System.ArgumentNullException">
    /// Thrown when <paramref name="ids"/> or <paramref name="clock"/> is null.
    /// </exception>
    public TextAppender(IdSequence ids, System.Func<long>? clock = null)
    {
        _ids = ids ?? throw new System.ArgumentNullException(nameof(ids));
        _clock = clock ?? (() => System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    /// <summary>
    /// Folds one chunk onto the message it belongs to, opening one when none is open.
    /// </summary>
    /// <param name="items">The transcript so far.</param>
    /// <param name="text">The chunk.</param>
    /// <param name="reasoning">Whether the chunk is reasoning rather than answer text.</param>
    /// <param name="source">Which model wrote it, when it was not the turn's own.</param>
    /// <returns>The transcript with the chunk folded in.</returns>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="items"/> is null.</exception>
    public IReadOnlyList<TranscriptItem> Append(
        IReadOnlyList<TranscriptItem> items,
        string text,
        bool reasoning,
        string? source = null)
    {
        if (items is null)
        {
            throw new System.ArgumentNullException(nameof(items));
        }

        // Answer text ends the thought in front of it: a model that has started
        // answering is no longer thinking, and a running clock would keep counting.
        var working = reasoning ? items : SealThought(items);
        var at = OpenSayIndex(working);
        var next = new List<TranscriptItem>(working);

        if (at >= 0 && next[at] is SayItem open)
        {
            next[at] = reasoning
                ? open with
                {
                    Reasoning = (open.Reasoning ?? string.Empty) + text,
                }
                : open with
                {
                    Text = open.Text + text,
                };
            return next;
        }

        var id = _ids.Next();
        next.Add(reasoning
            ? new SayItem { Id = id, Reasoning = text, Source = source, ThoughtStartedAt = _clock() }
            : new SayItem { Id = id, Text = text, Source = source });
        return next;
    }

    /// <summary>
    /// Stops the clock on a thought that is still arriving.
    /// </summary>
    /// <param name="items">The transcript so far.</param>
    /// <returns>
    /// The transcript with the open thought's duration recorded, or the same list
    /// when no thought is running one.
    /// </returns>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="items"/> is null.</exception>
    public IReadOnlyList<TranscriptItem> SealThought(IReadOnlyList<TranscriptItem> items)
    {
        if (items is null)
        {
            throw new System.ArgumentNullException(nameof(items));
        }

        var at = OpenSayIndex(items);
        if (at < 0 || items[at] is not SayItem open || string.IsNullOrEmpty(open.Reasoning) || open.ThoughtMs is not null)
        {
            return items;
        }

        var now = _clock();
        var elapsed = System.Math.Max(0, now - (open.ThoughtStartedAt ?? now));

        var next = new List<TranscriptItem>(items);
        next[at] = open with
        {
            ThoughtMs = elapsed,
            ThoughtStartedAt = null,
        };
        return next;
    }

    /// <summary>
    /// Settles the messages still being written.
    /// </summary>
    /// <param name="items">The transcript so far.</param>
    /// <param name="all">
    /// Whether to settle every open message rather than only the last. A turn that
    /// ends settles every one: a tool call between two answers leaves the earlier
    /// card unsealed forever.
    /// </param>
    /// <param name="cut">Why the turn ended early, when it did.</param>
    /// <returns>The transcript with the open messages settled.</returns>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="items"/> is null.</exception>
    public IReadOnlyList<TranscriptItem> Seal(IReadOnlyList<TranscriptItem> items, bool all, ThoughtCut? cut = null)
    {
        if (items is null)
        {
            throw new System.ArgumentNullException(nameof(items));
        }

        List<TranscriptItem>? next = null;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] is not SayItem say || say.Done)
            {
                continue;
            }

            // A cut marks only a thought still arriving: an answer that finished
            // was not cut short by the turn ending after it.
            var marked = cut is not null && !string.IsNullOrEmpty(say.Reasoning);
            var settled = say with
            {
                Done = true,
                ThoughtStartedAt = null,
                Cut = marked ? cut : say.Cut,
            };

            next ??= new List<TranscriptItem>(items);
            next[i] = settled;

            if (!all)
            {
                break;
            }
        }

        return next ?? items;
    }

    /// <summary>
    /// Finds the open message a chunk belongs to.
    /// </summary>
    /// <param name="items">The transcript so far.</param>
    /// <returns>
    /// The index of the message being written, or -1 when the last thing said was
    /// a settled message or a user's own message.
    /// </returns>
    private static int OpenSayIndex(IReadOnlyList<TranscriptItem> items)
    {
        for (var i = items.Count - 1; i >= 0; i--)
        {
            switch (items[i])
            {
                case SayItem say:
                    return say.Done ? -1 : i;
                case UserItem user when !user.Pending:
                    return -1;
            }
        }

        return -1;
    }
}
