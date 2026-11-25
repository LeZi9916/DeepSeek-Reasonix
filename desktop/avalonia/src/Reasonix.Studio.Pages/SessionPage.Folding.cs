using Avalonia.VisualTree;
using Reasonix.Studio.State.Transcript;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Reasonix.Studio.Pages;

public sealed partial class SessionPage
{
    /// <summary>Ids of tool rows belonging to the current running turn.</summary>
    private readonly HashSet<string> _liveWork = new(StringComparer.Ordinal);

    /// <summary>The device-local default when the reader has not touched a disclosure.</summary>
    private TranscriptFolding _folding = new();

    /// <summary>Disclosure choices of the pane currently mounted.</summary>
    private TranscriptDisclosureChoices _choices = new();

    /// <summary>Gets or sets the choices retained by the pane mounted in this window.</summary>
    public TranscriptDisclosureChoices DisclosureChoices
    {
        get
        {
            return _choices;
        }

        set
        {
            if (ReferenceEquals(_choices, value))
            {
                return;
            }

            _choices = value;
            _viewportVersion++;
            _state = new();
            _viewportRows.Clear();
            _rows.ClearRecent();
            _forms.Clear();
            _locations.Clear();
            _indices.Clear();
            _skipMountEntrance = true;
            _replySelection.Clear();
            _newAnswerAt = null;
            _scroll.Offset = default;
        }
    }

    /// <summary>Applies the same defaults shown in the settings preview.</summary>
    /// <param name="folding">The chosen local defaults.</param>
    public void SetTranscriptFolding(TranscriptFolding folding)
    {
        if (_folding != folding)
        {
            _folding = folding;
            Rebuild(_state.Items, recreate: true);
        }
    }

    /// <summary>Looks up a manual fold without replacing an unset choice with a default.</summary>
    /// <param name="part">The disclosure kind.</param><param name="id">The transcript identity.</param>
    /// <returns>The reader's choice if one exists.</returns>
    private bool? FoldChoice(TranscriptPart part, string id) => DisclosureChoices.Overrides.TryGetValue((part, id), out var open) ? open : null;

    /// <summary>Remembers a reader's choice across subsequent projections of the same row.</summary>
    /// <param name="part">The disclosure kind.</param><param name="id">The transcript identity.</param>
    /// <param name="open">Whether the reader expanded it.</param>
    private void RememberFold(TranscriptPart part, string id, bool open) => DisclosureChoices.Overrides[(part, id)] = open;

    /// <summary>Keeps live activity open between calls until the current turn settles.</summary>
    /// <param name="items">The transcript rows in turn order.</param>
    private void GatherLiveWork(IReadOnlyList<TranscriptItem> items)
    {
        _liveWork.Clear();
        if (!_state.Running)
        {
            return;
        }

        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (items[i] is UserItem { Pending: false })
            {
                break;
            }

            if (items[i] is ToolItem or ReadsItem)
            {
                _liveWork.Add(items[i].Id);
            }
        }
    }

    /// <summary>When a turn settles, updates its untouched activity groups without rebuilding answers.</summary>
    /// <param name="items">The mounted transcript.</param>
    private void RefreshLiveActivity(IReadOnlyList<TranscriptItem> items)
    {
        GatherLiveWork(items);
        if (_folding.Activity != TranscriptFoldMode.Live)
        {
            return;
        }

        if (_viewportRows.Any(entry => entry.Decoration == RowKind.Activity && entry.Children.Count > InlineChildren))
        {
            Rebuild(items);
        }

        foreach (var group in _rows.GetVisualDescendants().OfType<ActivityDisclosure>())
        {
            if (group.Tag is IReadOnlyList<TranscriptItem> work && FoldChoice(TranscriptPart.Activity, work[0].Id) is null)
            {
                group.SetOpen(ActivityOpen(work, ActivityTally.Of(work)));
            }
        }
    }
}
