using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Reasonix.Studio.State.Transcript;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Reasonix.Studio.Pages;

public sealed partial class SessionPage
{
    /// <summary>Whether a rail geometry read is queued for the next layout.</summary>
    private bool _railMeasureQueued;

    /// <summary>The newest explicit request to land on a user message.</summary>
    private CancellationTokenSource? _railJump;

    /// <summary>Loads saved bookmarks and connects changes to desktop persistence.</summary>
    /// <param name="bookmarks">Previously saved marks grouped by conversation.</param>
    /// <param name="save">Persists the updated marks of a saved conversation.</param>
    public void ConfigureMessageRailBookmarks(IReadOnlyDictionary<string, int[]> bookmarks, Action<string, int[]> save)
    {
        _messageRail.LoadBookmarks(bookmarks);
        _messageRail.BookmarksChanged += save;
    }

    /// <summary>Associates message stars with the selected conversation.</summary>
    /// <param name="scope">The canonical session identity, or empty before first save.</param>
    /// <param name="draftOwner">The current unsaved conversation's runtime.</param>
    public void SetMessageRailScope(string scope, object? draftOwner = null) => _messageRail.SetScope(scope, draftOwner);

    /// <summary>Folds the message rail with the desktop layout.</summary>
    /// <param name="visible">Whether the current window supports the rail.</param>
    public void SetMessageRailAvailable(bool visible) => _messageRail.SetAvailable(visible);

    /// <summary>Connects the rail to its viewport and the mounted rows.</summary>
    private void InitializeMessageRail()
    {
        _rows.SizeChanged += (_, _) => QueueRailMeasure();
        _messageRail.SizeChanged += (_, _) => QueueRailMeasure();
        _scroll.ScrollChanged += (_, change) =>
        {
            _messageLanding.Follow();
            if (change.ExtentDelta.Y != 0 || change.ViewportDelta.Y != 0)
            {
                QueueRailMeasure();
            }
            else
            {
                _messageRail.ShowViewport(_scroll);
            }
        };
        _messageRail.JumpRequested += JumpToTranscriptRow;
        _scroll.PointerWheelChanged += (_, _) => CancelRailJump();
        _scroll.DetachedFromVisualTree += (_, _) => CancelRailJump();
    }

    /// <summary>Glides to a mounted transcript row using the reference's 300 ms cubic exit.</summary>
    /// <param name="row">The destination row.</param>
    private async void JumpToTranscriptRow(Control row)
    {
        CancelRailJump();
        if (row.TranslatePoint(default, _scroll) is not Point point)
        {
            return;
        }

        var from = _scroll.Offset;
        var destination = new Vector(from.X, Math.Clamp(from.Y + point.Y - 12, 0, _scroll.ScrollBarMaximum.Y));
        var motionEnabled = TopLevel.GetTopLevel(this) is Window window && window.Classes.Contains("motion");
        if (!motionEnabled || Math.Abs(destination.Y - from.Y) < 1)
        {
            _scroll.Offset = destination;
            if (motionEnabled)
            {
                FlashTranscriptLanding(row);
            }

            return;
        }

        using var jump = new CancellationTokenSource();
        _railJump = jump;
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(300),
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(ScrollViewer.OffsetProperty, from) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(ScrollViewer.OffsetProperty, destination) } },
            },
        };
        try
        {
            await animation.RunAsync(_scroll, jump.Token).ConfigureAwait(true);
            if (_railJump == jump)
            {
                _scroll.Offset = destination;
                FlashTranscriptLanding(row);
            }
        }
        catch (OperationCanceledException) when (jump.IsCancellationRequested)
        {
        }
        finally
        {
            if (_railJump == jump)
            {
                _railJump = null;
            }
        }
    }

    /// <summary>Marks the message or tool call reached by the latest jump.</summary>
    /// <param name="row">The realized transcript row.</param>
    private void FlashTranscriptLanding(Control row)
    {
        if (row is Border { Child: UserMessage message } && message.FindControl<Border>("Bubble") is { } bubble)
        {
            _messageLanding.Play(bubble);
        }
        else if (row is Border { Child: ToolCallHeader tool })
        {
            _ = tool.FlashLandingAsync();
        }
    }

    /// <summary>Lets a newer scroll request take precedence over a rail jump.</summary>
    private void CancelRailJump()
    {
        if (_railJump is { } jump)
        {
            var offset = _scroll.Offset;
            _railJump = null;
            jump.Cancel();
            _scroll.Offset = offset;
        }
    }

    /// <summary>Assigns the mounted user rows to the message track.</summary>
    private void RefreshMessageRail()
    {
        CancelRailJump();
        _messageLanding.Clear();
        _messageRail.SetMarks(
            _drawn.Where(row => row.Tag is UserItem).Select(row => ((UserItem)row.Tag!, row)).ToArray(),
            Language);
        QueueRailMeasure();
    }

    /// <summary>Measures once after layout has positioned the reading column.</summary>
    private void QueueRailMeasure()
    {
        if (_railMeasureQueued)
        {
            return;
        }

        _railMeasureQueued = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                _railMeasureQueued = false;
                _messageRail.MeasureMarks(_scroll);
            },
            DispatcherPriority.Loaded);
    }
}
