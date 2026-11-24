using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using System;
using System.Diagnostics;

namespace Reasonix.Studio.Shell.Composer;

/// <summary>Animates the mounted composer popup while preserving input routing during its exit.</summary>
internal sealed class ComposerMenuMotion
{
    /// <summary>The easing shared by the reference menu's opacity and transform.</summary>
    private static readonly SplineEasing s_ease = new(0.16, 1, 0.3, 1);

    /// <summary>The menu control in the owner's visual tree.</summary>
    private readonly Control _menu;

    /// <summary>The overlay that remains mounted until the exit completes.</summary>
    private readonly Popup _popup;

    /// <summary>The painted frame inside the overlay.</summary>
    private readonly Control _frame;

    /// <summary>The vertical movement of the painted menu.</summary>
    private readonly TranslateTransform _offset = new();

    /// <summary>The vertical scale around the trigger-facing edge.</summary>
    private readonly ScaleTransform _scale = new();

    /// <summary>The top level subscribed for dismissal input.</summary>
    private TopLevel? _owner;

    /// <summary>The trigger receiving focus when the menu is dismissed with the keyboard.</summary>
    private Control? _anchor;

    /// <summary>The trigger group whose own clicks handle toggling.</summary>
    private Control? _inside;

    /// <summary>The active transition and its start time, if moving.</summary>
    private Travel? _travel;

    /// <summary>The currently rendered visibility, from hidden to shown.</summary>
    private double _pose;

    /// <summary>The configured time of a complete transition.</summary>
    private int _milliseconds;

    /// <summary>Whether a frame callback is already registered.</summary>
    private bool _frameScheduled;

    /// <summary>Initializes a new instance of the <see cref="ComposerMenuMotion"/> class.</summary>
    /// <param name="menu">The composer control that owns the popup.</param>
    /// <param name="popup">The native overlay.</param>
    /// <param name="frame">The content displayed within the overlay.</param>
    internal ComposerMenuMotion(Control menu, Popup popup, Control frame)
    {
        _menu = menu;
        _popup = popup;
        _frame = frame;
        frame.RenderTransform = new TransformGroup { Children = { _scale, _offset } };
        ApplyPose(0);
        menu.AttachedToVisualTree += (_, _) => AttachOwner();
        menu.DetachedFromVisualTree += (_, _) => DetachOwner();
        popup.Closed += (_, _) => PopupClosed();
    }

    /// <summary>Raised after the physical overlay closes and its content can be reset.</summary>
    internal event Action? Closed;

    /// <summary>Gets a value indicating whether the menu is accepting interaction.</summary>
    internal bool IsOpen
    {
        get; private set;
    }

    /// <summary>Opens the native overlay and reverses a still-painted exit if needed.</summary>
    /// <param name="anchor">The control that opened the menu.</param>
    /// <param name="milliseconds">The reference duration in milliseconds.</param>
    /// <param name="inside">An optional group of equivalent triggers.</param>
    internal void Open(Control anchor, int milliseconds, Control? inside = null)
    {
        if (IsOpen)
        {
            return;
        }

        _anchor = anchor;
        _inside = inside ?? anchor;
        _milliseconds = milliseconds;
        _anchor.Classes.Set("open", true);
        _frame.IsHitTestVisible = true;
        IsOpen = true;
        Move(1);
        _popup.IsOpen = true;
    }

    /// <summary>Dismisses the menu and immediately releases its input surface.</summary>
    /// <param name="immediately">Whether the owning view is being replaced.</param>
    internal void Close(bool immediately = false)
    {
        if (!IsOpen && !immediately)
        {
            return;
        }

        IsOpen = false;
        _anchor?.Classes.Remove("open");
        _frame.IsHitTestVisible = false;
        if (!immediately && _frame.IsKeyboardFocusWithin)
        {
            _anchor?.Focus();
        }

        if (immediately)
        {
            _travel = null;
            ApplyPose(0);
            _popup.IsOpen = false;
        }
        else
        {
            Move(0);
        }
    }

    /// <summary>Starts from the displayed pose rather than jumping during a reversal.</summary>
    /// <param name="target">One for entry and zero for departure.</param>
    private void Move(double target)
    {
        if (_owner is null || !_menu.IsEffectivelyVisible || !_owner.Classes.Contains("motion") || _milliseconds <= 0 || _pose == target)
        {
            _travel = null;
            ApplyPose(target);
            if (target == 0)
            {
                _popup.IsOpen = false;
            }

            return;
        }

        var duration = _milliseconds * Math.Abs(target - _pose);
        _travel = new Travel(_pose, target, Stopwatch.GetTimestamp(), duration);
        RequestNextFrame();
    }

    /// <summary>Schedules at most one callback even when the menu reverses direction.</summary>
    private void RequestNextFrame()
    {
        if (!_frameScheduled && _owner is { } owner)
        {
            _frameScheduled = true;
            owner.RequestAnimationFrame(Frame);
        }
    }

    /// <summary>Paints the transition and retires the overlay after departure.</summary>
    /// <param name="elapsed">The top-level compositor timestamp.</param>
    private void Frame(TimeSpan elapsed)
    {
        _frameScheduled = false;
        if (_travel is not { } travel || _owner is not { } owner)
        {
            return;
        }

        var amount = owner.Classes.Contains("motion") && _menu.IsEffectivelyVisible
            ? Math.Clamp(Stopwatch.GetElapsedTime(travel.When).TotalMilliseconds / travel.Duration, 0, 1)
            : 1;
        ApplyPose(travel.From + ((travel.To - travel.From) * s_ease.Ease(amount)));
        if (amount >= 1)
        {
            _travel = null;
            if (travel.To == 0)
            {
                _popup.IsOpen = false;
            }
        }
        else
        {
            RequestNextFrame();
        }
    }

    /// <summary>Applies the same opacity, lift and vertical scale from a single transition.</summary>
    /// <param name="visible">The rendered fraction.</param>
    private void ApplyPose(double visible)
    {
        _pose = visible;
        _frame.Opacity = visible;
        _offset.Y = -4 * (1 - visible);
        _scale.ScaleY = 0.985 + (0.015 * visible);
    }

    /// <summary>Observes dismissal input without using the popup's click-blocking light-dismiss layer.</summary>
    private void AttachOwner()
    {
        _owner = TopLevel.GetTopLevel(_menu);
        _owner?.AddHandler(InputElement.PointerPressedEvent, OnPointer, RoutingStrategies.Tunnel, true);
        _owner?.AddHandler(InputElement.KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
    }

    /// <summary>Disposes of the overlay and handlers when the owning view detaches.</summary>
    private void DetachOwner()
    {
        Close(immediately: true);
        _owner?.RemoveHandler(InputElement.PointerPressedEvent, OnPointer);
        _owner?.RemoveHandler(InputElement.KeyDownEvent, OnKey);
        _owner = null;
    }

    /// <summary>Retires the visual state after teardown, including when the native host closes.</summary>
    private void PopupClosed()
    {
        IsOpen = false;
        _travel = null;
        _anchor?.Classes.Remove("open");
        _anchor = _inside = null;
        ApplyPose(0);
        Closed?.Invoke();
    }

    /// <summary>Lets outside pointer presses continue to their normal targets.</summary>
    /// <param name="sender">The owning top level.</param>
    /// <param name="args">The original pointer input.</param>
    private void OnPointer(object? sender, PointerPressedEventArgs args)
    {
        if (IsOpen && args.Source is Visual hit &&
            !ReferenceEquals(hit, _frame) && !_frame.IsVisualAncestorOf(hit) &&
            !ReferenceEquals(hit, _inside) && _inside?.IsVisualAncestorOf(hit) != true)
        {
            Close();
        }
    }

    /// <summary>Dismisses the active menu on Escape and restores the original focus.</summary>
    /// <param name="sender">The owning top level.</param>
    /// <param name="args">The original keyboard input.</param>
    private void OnKey(object? sender, KeyEventArgs args)
    {
        if (IsOpen && args.Key == Key.Escape)
        {
            var anchor = _anchor;
            Close();
            anchor?.Focus();
            args.Handled = true;
        }
    }

    /// <summary>One transition between two visibility fractions.</summary>
    /// <param name="From">The initial pose.</param>
    /// <param name="To">The destination pose.</param>
    /// <param name="When">The monotonic start time.</param>
    /// <param name="Duration">The travel time in milliseconds.</param>
    private readonly record struct Travel(double From, double To, long When, double Duration);
}
