using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.State.Transcript;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Reasonix.Studio.Pages;

public sealed partial class SessionPage
{
    /// <summary>The endpoint controls over the scroll viewport.</summary>
    private readonly TranscriptJump _transcriptJump = new();

    /// <summary>Whether the plan extends over the lower edge of the transcript.</summary>
    private bool _jumpBehindPlan;

    /// <summary>Whether an explicit request is following the newest message.</summary>
    private bool _followingLatest;

    /// <summary>Saves an explicit reading location or returns null to follow the tail.</summary>
    /// <returns>The location to restore when focusing the session again.</returns>
    public (string? Id, double Offset, double Inset)? ReadSessionPosition() =>
        _followingLatest || _scroll.ScrollBarMaximum.Y - _scroll.Offset.Y <= 2 ? null : ReadAnchor();

    /// <summary>Restores a saved reading location or the tail of a newly opened session.</summary>
    /// <param name="position">The explicit reading location.</param>
    public void RestoreSessionPosition((string? Id, double Offset, double Inset)? position)
    {
        PauseTranscriptFollow();
        if (position is { } anchor)
        {
            RestoreAnchor(anchor);
            return;
        }

        _followingLatest = true;
        if (_scroll.IsEffectivelyVisible && _viewportRows.Count > 0)
        {
            _rows.ScrollIntoView(_viewportRows.Count - 1);
            _scroll.ScrollToEnd();
        }

        FollowEnd();
    }

    /// <summary>Keeps the endpoint controls clear of the plan strip.</summary>
    /// <param name="visible">Whether the plan strip occupies the edge.</param>
    public void SetJumpPlanClearance(bool visible)
    {
        _jumpBehindPlan = visible;
        UpdateTranscriptJump();
    }

    /// <summary>Relinquishes the live tail when the reader requests a scroll.</summary>
    public void PauseTranscriptFollow()
    {
        _followingLatest = false;
        _anchorTicket++;
        CancelRailJump();
    }

    /// <summary>Finds the visible headers of expanded tools and thoughts inside a rendered row.</summary>
    /// <param name="root">The realized subtree.</param>
    /// <returns>The eligible headers in transcript order.</returns>
    private static IEnumerable<Button> ExpandedHeadings(Control root)
    {
        foreach (var detail in root.GetVisualDescendants().OfType<DisclosureMotion>())
        {
            if (!detail.Expanded || !detail.IsEffectivelyVisible)
            {
                continue;
            }

            var owner = detail.GetVisualAncestors().OfType<UserControl>().FirstOrDefault();
            if (owner is ToolCallHeader && detail.Name == "Detail" && owner.FindControl<Button>("Toggle") is { } tool)
            {
                yield return tool;
            }
            else if (owner is AnswerCard && detail.Name == "ReasoningBody" && owner.FindControl<Button>("Thought") is { } thought)
            {
                yield return thought;
            }
        }
    }

    /// <summary>Connects the endpoint controls and follows a changing tail extent.</summary>
    private void InitializeTranscriptJump()
    {
        _transcriptJump.Requested += MoveTranscript;
        _messageRail.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty)
            {
                UpdateTranscriptJump();
            }
        };
        _scroll.AddHandler(
            PointerPressedEvent,
            (_, e) =>
            {
                if (e.Pointer.Type != PointerType.Mouse || (e.Source is Visual origin && origin.GetSelfAndVisualAncestors().Any(visual => visual is ScrollBar)))
                {
                    PauseTranscriptFollow();
                }
            },
            RoutingStrategies.Tunnel);
        _scroll.AddHandler(
            KeyDownEvent,
            (_, e) =>
            {
                if (e.Key is Key.PageUp or Key.PageDown or Key.Home or Key.End or Key.Up or Key.Down)
                {
                    PauseTranscriptFollow();
                }
            },
            RoutingStrategies.Tunnel);
        _scroll.PropertyChanged += (_, e) =>
        {
            if (e.Property == ScrollViewer.OffsetProperty || e.Property == ScrollViewer.ExtentProperty || e.Property == ScrollViewer.ViewportProperty)
            {
                UpdateTranscriptJump();
            }
        };
        _scroll.ScrollChanged += (_, e) =>
        {
            var changed = e.ExtentDelta != default || e.ViewportDelta != default;
            var displaced = e.OffsetDelta != default && _scroll.ScrollBarMaximum.Y - _scroll.Offset.Y > 2;
            if (_followingLatest && (changed || displaced))
            {
                FollowEnd();
            }
        };
    }

    /// <summary>Projects scroll reach into the independent floating controls.</summary>
    private void UpdateTranscriptJump() => _transcriptJump.Show(
        _scroll.Offset.Y, _scroll.ScrollBarMaximum.Y - _scroll.Offset.Y, _messageRail.IsVisible, _jumpBehindPlan, Language);

    /// <summary>Moves to an endpoint or steps between expanded details.</summary>
    /// <param name="down">Whether the target is towards the latest message.</param>
    /// <param name="step">Whether to try a detail before its endpoint.</param>
    private void MoveTranscript(bool down, bool step)
    {
        PauseTranscriptFollow();
        if (step && TryStepTranscript(down))
        {
            return;
        }

        if (down)
        {
            _followingLatest = true;
            FollowEnd();
        }
        else
        {
            _rows.ScrollIntoView(0);
            _scroll.Offset = _scroll.Offset.WithY(0);
        }
    }

    /// <summary>Locates the nearest expanded block beyond the visible reading area.</summary>
    /// <param name="down">Whether to search after the view.</param>
    /// <returns>Whether an expanded block was reached.</returns>
    private bool TryStepTranscript(bool down)
    {
        var top = _scroll.Offset.Y;
        var bounds = _scroll.Viewport.Height;
        var mounted = ExpandedHeadings(_rows)
            .Select(heading => heading.TranslatePoint(default, _scroll)?.Y)
            .OfType<double>()
            .Where(y => down ? y > bounds - 4 : y < -4)
            .OrderBy(y => down ? y : -y)
            .FirstOrDefault(double.NaN);
        if (!double.IsNaN(mounted))
        {
            LandAt(top + mounted);
            return true;
        }

        var positions = _realized.Keys.Where(_locations.ContainsKey).Select(id => _locations[id]).ToArray();
        if (positions.Length == 0)
        {
            return false;
        }

        var edge = down ? positions.Max() : positions.Min();
        var direction = down ? 1 : -1;
        for (var index = edge + direction; (uint)index < (uint)_viewportRows.Count; index += direction)
        {
            var entry = _viewportRows[index];
            if (!MayContainExpandedDetail(entry))
            {
                continue;
            }

            _rows.ScrollIntoView(index);
            _scroll.UpdateLayout();
            if (_rows.ContainerFromIndex(index) is not Control container)
            {
                continue;
            }

            var headings = ExpandedHeadings(container);
            var candidate = down ? headings.FirstOrDefault() : headings.LastOrDefault();
            if (candidate?.TranslatePoint(default, _scroll) is { } point)
            {
                LandAt(_scroll.Offset.Y + point.Y);
                return true;
            }
        }

        return false;

        void LandAt(double position)
        {
            var destination = Math.Clamp(position - 8, 0, _scroll.ScrollBarMaximum.Y);
            if (down && _scroll.ScrollBarMaximum.Y - destination < TranscriptJump.Edge)
            {
                _followingLatest = true;
                FollowEnd();
            }
            else
            {
                _scroll.Offset = _scroll.Offset.WithY(destination);
            }
        }
    }

    /// <summary>Skips virtual entries with no potential step destination.</summary>
    /// <param name="entry">The projected row.</param>
    /// <returns>Whether to realize the row for its expansion state.</returns>
    private bool MayContainExpandedDetail(ViewportRow entry) =>
        (entry.Lead is not null && Detail(entry.Lead)) || entry.Children.Any(Detail);

    /// <summary>Reads a tool or thought's open policy without mounting its view.</summary>
    /// <param name="item">Canonical row.</param>
    /// <returns>Whether it can contain an expanded detail.</returns>
    private bool Detail(TranscriptItem item) => item switch
    {
        SayItem { Reasoning.Length: > 0 } say => FoldChoice(TranscriptPart.Thinking, say.Id)
            ?? _folding.StartsOpen(TranscriptPart.Thinking, live: !say.Done && say.Text.Length == 0),
        ToolItem tool => FoldChoice(TranscriptPart.Steps, tool.Id)
            ?? _folding.StartsOpen(TranscriptPart.Steps, failed: !string.IsNullOrEmpty(tool.Call.Err), changed: TranscriptFolding.ChangedFile(tool.Call)),
        _ => false,
    };
}
