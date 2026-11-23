using System.Collections.Generic;

namespace Reasonix.Studio.State.Transcript;

/// <summary>A pane's last unread span, held in place when the reader starts another turn.</summary>
public sealed class NewAnswerBoundary
{
    /// <summary>The host-reported cutoff for the current span.</summary>
    private long? _viewedAt;

    /// <summary>The answer or user turn where the divider belongs.</summary>
    private string? _anchor;

    /// <summary>Whether a nonempty transcript gave this span a position.</summary>
    private bool _resolved;

    /// <summary>Whether the pane has started a turn since the cutoff arrived.</summary>
    private bool _frozen;

    /// <summary>Adopts a new unread span without moving an already-held span with the same cutoff.</summary>
    /// <param name="viewedAt">The last-viewed timestamp known to the viewer.</param>
    public void Remember(long? viewedAt)
    {
        if (_viewedAt != viewedAt)
        {
            _viewedAt = viewedAt;
            _anchor = null;
            _frozen = _resolved = false;
        }
    }

    /// <summary>Projects the divider position from the newest rows and the current turn state.</summary>
    /// <param name="items">The transcript.</param><param name="running">Whether it has an active turn.</param>
    /// <returns>The row the divider belongs before, if known.</returns>
    public string? Resolve(IReadOnlyList<TranscriptItem> items, bool running)
    {
        if (_viewedAt is not > 0 || _frozen || items.Count == 0)
        {
            return _anchor;
        }

        if (!_resolved || !running)
        {
            var index = NewAnswerMarker.PlaceOf(items, _viewedAt);
            _anchor = index >= 0 ? items[index].Id : null;
        }

        _frozen = running;
        _resolved = true;
        return _anchor;
    }
}
