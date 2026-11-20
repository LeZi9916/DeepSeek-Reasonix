using Reasonix.Studio.Contracts.Queue;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Reasonix.Studio.Pages.Composer;

/// <summary>
/// Turns the queue the kernel holds into the strip a reader sees.
/// </summary>
/// <remarks>
/// Ported from the reference frontend's <c>ui/Queue.tsx</c>. The rules matter
/// more than the rows: a line the kernel already took is not waiting any more, a
/// host continuation is not the person's, and a retry is only offered where the
/// kernel knows the line never reached the model — a retry of anything else may
/// repeat an effect the person never asked for twice.
/// </remarks>
public static class QueuePresenter
{
    /// <summary>
    /// The codes whose entry the kernel knows never reached the model.
    /// </summary>
    /// <remarks>
    /// An empty list is not "retry nothing"; it is "only these". Everything else
    /// uncertain may already have run.
    /// </remarks>
    private static readonly string[] s_retrySafe = { "steer_unapplied" };

    /// <summary>
    /// Reads the lines nobody has acted on yet, in dispatch order.
    /// </summary>
    /// <param name="queue">The queue as the kernel holds it, or null when it could not be read.</param>
    /// <returns>The waiting lines; empty for an unreadable queue.</returns>
    public static IReadOnlyList<QueueItem> Waiting(QueueSnapshot? queue)
    {
        if (queue is null)
        {
            return Array.Empty<QueueItem>();
        }

        var rows = new List<QueueItem>(queue.Items.Count);
        foreach (var item in queue.Items)
        {
            if (Taken(item.State) || !Theirs(item))
            {
                continue;
            }

            rows.Add(item);
        }

        return rows;
    }

    /// <summary>
    /// Builds the strip for one queue.
    /// </summary>
    /// <param name="queue">The queue as the kernel holds it, or null when unreadable.</param>
    /// <param name="running">Whether a turn is running, which is what "send now" has to interrupt.</param>
    /// <param name="language">The language to say the labels in.</param>
    /// <returns>One row per waiting line, in dispatch order.</returns>
    public static IReadOnlyList<QueueRow> Rows(QueueSnapshot? queue, bool running, InterfaceLanguage language)
    {
        var waiting = Waiting(queue);
        var rows = new List<QueueRow>(waiting.Count);
        var sendable = running && queue is { Paused: false } && !queue.Readonly;

        for (var index = 0; index < waiting.Count; index++)
        {
            var item = waiting[index];
            var why = WhyOf(item);
            rows.Add(new QueueRow(
                item.Id,
                Label(item, index + 1, language),
                item.Preview,
                why,
                sendable && Steerable(item),
                sendable && (item.State == "queued" || item.State == "steer_accepted"),
                Retryable(item),
                queue is { Readonly: false }));
        }

        return rows;
    }

    /// <summary>
    /// Reports whether the queue's head should offer to release dispatch.
    /// </summary>
    /// <param name="queue">The queue as the kernel holds it.</param>
    /// <returns>True when dispatch is held on an editable queue.</returns>
    public static bool OffersRelease(QueueSnapshot? queue)
    {
        return queue is { Paused: true, Readonly: false };
    }

    /// <summary>
    /// Reports whether an entry has already left the strip.
    /// </summary>
    /// <param name="state">The entry's state.</param>
    /// <returns>True once the kernel took the line into the transcript.</returns>
    public static bool Taken(string state)
    {
        return state == "steer_consumed" || state == "running";
    }

    /// <summary>
    /// Reports whether an entry belongs on the strip at all.
    /// </summary>
    /// <param name="item">The entry.</param>
    /// <returns>
    /// True for anything the person wrote, and for a host continuation only when
    /// it needs a decision.
    /// </returns>
    public static bool Theirs(QueueItem item)
    {
        return item.Origin != "host" || item.State == "blocked" || item.State == "uncertain";
    }

    /// <summary>
    /// Reports whether an entry can become guidance for the running turn.
    /// </summary>
    /// <param name="item">The entry.</param>
    /// <returns>
    /// True only for a plain queued follow-up: a picture or a frozen reference
    /// travels with a whole turn, and guidance carries plain text.
    /// </returns>
    public static bool Steerable(QueueItem item)
    {
        return item.Origin != "host"
            && item.State == "queued"
            && item.Intent == "followup"
            && !item.Images
            && item.Refs.Count == 0;
    }

    /// <summary>
    /// Reports whether the kernel offers a retry of this entry.
    /// </summary>
    /// <param name="item">The entry.</param>
    /// <returns>True for a blocked entry, or an uncertain one known not to have run.</returns>
    /// <remarks>
    /// The kernel refuses a retry that may repeat an effect; the strip does not
    /// offer one either, because an offer the kernel answers with a conflict is
    /// a worse answer than no button.
    /// </remarks>
    public static bool Retryable(QueueItem item)
    {
        if (item.State == "blocked")
        {
            return true;
        }

        if (item.State != "uncertain")
        {
            return false;
        }

        return Array.IndexOf(s_retrySafe, item.BlockCode) >= 0;
    }

    /// <summary>
    /// Reads what one line is, in the reader's language.
    /// </summary>
    /// <param name="item">The entry.</param>
    /// <param name="position">Its place in the strip, counting from one.</param>
    /// <param name="language">The language to say it in.</param>
    /// <returns>The line's label.</returns>
    public static string Label(QueueItem item, int position, InterfaceLanguage language)
    {
        if (item.Origin == "host")
        {
            return item.State switch
            {
                "blocked" => MessageCatalog.Get(MessageKey.Composer.BackgroundBlocked, language),
                "uncertain" => MessageCatalog.Get(MessageKey.Composer.BackgroundUncertain, language),
                _ => MessageCatalog.Get(MessageKey.Composer.BackgroundJobFollowOn, language),
            };
        }

        switch (item.State)
        {
            case "steer_accepted":
                return MessageCatalog.Get(MessageKey.Composer.SteerAccepted, language);

            case "blocked":
                return MessageCatalog.Get(MessageKey.Composer.Blocked, language);

            case "uncertain":
                return MessageCatalog.Get(MessageKey.Composer.Uncertain, language);

            default:
                if (item.Images && item.State == "queued")
                {
                    return MessageCatalog.Get(MessageKey.Composer.HasAnImageSentWhen, language);
                }

                if (item.Intent == "steer")
                {
                    return MessageCatalog.Get(MessageKey.Composer.SteerQueued, language);
                }

                // The reference names a waiting follow-up by its number in the
                // queue, not by a single word: "queued 1" says which line it is.
                return MessageCatalog.Get(MessageKey.Common.Queued, language)
                    + " "
                    + position.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Reads the sentence under a held line, when there is one.
    /// </summary>
    /// <param name="item">The entry.</param>
    /// <returns>The kernel's own words, or null when the entry is not held.</returns>
    private static string? WhyOf(QueueItem item)
    {
        if (item.State != "blocked" && item.State != "uncertain")
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(item.BlockReason) ? null : item.BlockReason;
    }
}
