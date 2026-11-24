using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Controls;

/// <summary>An anchored card that supports hover, keyboard focus and explicit pinning.</summary>
public sealed partial class StudioHoverCard : UserControl
{
    /// <summary>Defines the trigger control.</summary>
    public static readonly StyledProperty<Control?> AnchorProperty = AvaloniaProperty.Register<StudioHoverCard, Control?>(nameof(Anchor));

    /// <summary>Defines the detail content.</summary>
    public static readonly StyledProperty<Control?> CardProperty = AvaloniaProperty.Register<StudioHoverCard, Control?>(nameof(Card));

    /// <summary>Defines whether clicking the trigger pins this card.</summary>
    public static readonly StyledProperty<bool> PinOnClickProperty = AvaloniaProperty.Register<StudioHoverCard, bool>(nameof(PinOnClick));

    /// <summary>Defines whether keyboard focus opens the detail.</summary>
    public static readonly StyledProperty<bool> OpenOnFocusProperty = AvaloniaProperty.Register<StudioHoverCard, bool>(nameof(OpenOnFocus));

    /// <summary>Defines the preferred card width.</summary>
    public static readonly StyledProperty<double> CardWidthProperty = AvaloniaProperty.Register<StudioHoverCard, double>(nameof(CardWidth), 326);

    /// <summary>Defines the trigger-to-card gap.</summary>
    public static readonly StyledProperty<double> GapProperty = AvaloniaProperty.Register<StudioHoverCard, double>(nameof(Gap), 8);

    /// <summary>Bridges the small gap between trigger and floating card.</summary>
    private readonly DispatcherTimer _leave = new() { Interval = TimeSpan.FromMilliseconds(110) };

    /// <summary>The transform applied to the card about its anchor edge.</summary>
    private readonly MatrixTransform _anchorTransform = new();

    /// <summary>The current owner for keyboard dismissal.</summary>
    private TopLevel? _owner;

    /// <summary>Whether the card stays open independently of the pointer.</summary>
    private bool _pinned;

    /// <summary>Prevents keyboard focus restoration from reopening a dismissed tooltip.</summary>
    private bool _restoringFocus;

    /// <summary>Cancels a reveal superseded by another pointer or keyboard action.</summary>
    private CancellationTokenSource? _reveal;

    /// <summary>Whether the visible surface is completing its exit transition.</summary>
    private bool _hiding;

    /// <summary>Initializes a new instance of the <see cref="StudioHoverCard"/> class.</summary>
    public StudioHoverCard()
    {
        InitializeComponent();
        ActualThemeVariantChanged += (_, _) => UpdateShadow();
        UpdateShadow();
        Frame.PropertyChanged += (_, args) =>
        {
            if (ScaleFromAnchor && args.Property == OpacityProperty)
            {
                UpdateAnchorTransform();
            }
        };
        AnchorPresenter.PointerEntered += (_, _) =>
        {
            if (CanHover?.Invoke() != false)
            {
                Open();
            }
        };
        AnchorPresenter.PointerExited += (_, _) => ScheduleClose();
        Frame.PointerEntered += (_, _) => _leave.Stop();
        Frame.PointerExited += (_, _) => ScheduleClose();
        AnchorPresenter.GotFocus += (_, _) =>
        {
            if (OpenOnFocus && !_restoringFocus)
            {
                Open();
            }
        };
        AnchorPresenter.LostFocus += (_, _) => ScheduleClose();
        AnchorPresenter.AddHandler(Button.ClickEvent, (_, _) =>
        {
            if (!PinOnClick)
            {
                return;
            }

            _pinned = !_pinned;
            if (_pinned)
            {
                Open();
                Pinned?.Invoke();
            }
            else if (!ScaleFromAnchor || CanHover?.Invoke() == false || !AnchorPresenter.IsPointerOver)
            {
                Close();
            }
        });
        _leave.Tick += (_, _) =>
        {
            _leave.Stop();
            if (!_pinned && !(OpenOnFocus && Anchor?.IsKeyboardFocusWithin == true) && !Frame.IsPointerOver && !AnchorPresenter.IsPointerOver)
            {
                Close();
            }
        };
        Surface.Closed += (_, _) => _pinned = false;
        DetachedFromVisualTree += (_, _) =>
        {
            Close(true);
            DetachOwner();
        };
        AttachedToVisualTree += (_, _) =>
        {
            _owner = TopLevel.GetTopLevel(this);
            if (_owner is not null)
            {
                _owner.Classes.CollectionChanged += MotionChanged;
            }

            _owner?.AddHandler(KeyDownEvent, OnOwnerKey, RoutingStrategies.Tunnel);
            _owner?.AddHandler(PointerPressedEvent, OnOwnerPointer, RoutingStrategies.Tunnel, true);
        };
    }

    /// <summary>Raised when a trigger explicitly pins its card.</summary>
    public event Action? Pinned;

    /// <summary>Gets or sets the presentation guard for pointer-only reveals.</summary>
    public Func<bool>? CanHover
    {
        get; set;
    }

    /// <summary>Gets or sets the trigger control.</summary>
    public Control? Anchor
    {
        get => GetValue(AnchorProperty); set => SetValue(AnchorProperty, value);
    }

    /// <summary>Gets or sets detail content.</summary>
    public Control? Card
    {
        get => GetValue(CardProperty); set => SetValue(CardProperty, value);
    }

    /// <summary>Gets or sets a value indicating whether clicks pin the detail.</summary>
    public bool PinOnClick
    {
        get => GetValue(PinOnClickProperty); set => SetValue(PinOnClickProperty, value);
    }

    /// <summary>Gets or sets a value indicating whether keyboard focus opens the detail.</summary>
    public bool OpenOnFocus
    {
        get => GetValue(OpenOnFocusProperty); set => SetValue(OpenOnFocusProperty, value);
    }

    /// <summary>Gets or sets the preferred width before viewport clamping.</summary>
    public double CardWidth
    {
        get => GetValue(CardWidthProperty); set => SetValue(CardWidthProperty, value);
    }

    /// <summary>Gets or sets a value indicating whether the popup opens above the anchor's right edge.</summary>
    public bool AlignRight
    {
        get; set;
    }

    /// <summary>Gets or sets a value indicating whether the card scales and slides from the anchor as it fades.</summary>
    public bool ScaleFromAnchor
    {
        get; set;
    }

    /// <summary>Gets or sets the fraction of the viewport available to the card.</summary>
    public double CardHeightFraction
    {
        get; set;
    } = 1;

    /// <summary>Gets a value indicating whether the card is visible.</summary>
    public bool IsOpen => Surface.IsOpen && !_hiding;

    /// <summary>Gets or sets the trigger-to-card gap in device-independent pixels.</summary>
    public double Gap
    {
        get => GetValue(GapProperty); set => SetValue(GapProperty, value);
    }

    /// <summary>Gets a value indicating whether the card is explicitly pinned.</summary>
    public bool IsPinned => _pinned;

    /// <summary>Opens the card without taking keyboard focus.</summary>
    public void Open()
    {
        _leave.Stop();
        if (IsOpen)
        {
            return;
        }

        var from = ScaleFromAnchor && Surface.IsOpen ? Frame.Opacity : 0;
        _reveal?.Cancel();
        _hiding = false;
        Frame.IsHitTestVisible = true;
        Surface.VerticalOffset = -Gap;
        Surface.Placement = AlignRight ? PlacementMode.TopEdgeAlignedRight : PlacementMode.TopEdgeAlignedLeft;
        var size = TopLevel.GetTopLevel(this)?.ClientSize ?? new Size(760, 480);
        Frame.Width = Math.Min(CardWidth, Math.Max(120, size.Width - 32));
        CardScroll.MaxHeight = Math.Max(80, Math.Min(size.Height - 32, (size.Height * CardHeightFraction) - 2));
        CardScroll.Classes.Set("anchor-card", ScaleFromAnchor);
        var animate = _owner?.Classes.Contains("motion") == true;
        Frame.Opacity = animate ? from : 1;
        if (ScaleFromAnchor)
        {
            Frame.RenderTransformOrigin = new RelativePoint(AlignRight ? 1 : 0, 1, RelativeUnit.Relative);
            Frame.RenderTransform = _anchorTransform;
            UpdateAnchorTransform();
        }

        Surface.IsOpen = true;
        if (animate)
        {
            _ = FadeAsync(false, from);
        }
    }

    /// <summary>Closes hover and pinned presentations.</summary>
    /// <param name="immediately">Whether teardown must skip the exit transition.</param>
    public void Close(bool immediately = false)
    {
        _leave.Stop();
        _pinned = false;
        if (_hiding && !immediately)
        {
            return;
        }

        var from = ScaleFromAnchor ? Frame.Opacity : 1;
        _reveal?.Cancel();
        if (!immediately && Surface.IsOpen && _owner is { IsVisible: true } && _owner.Classes.Contains("motion"))
        {
            _hiding = true;
            Frame.IsHitTestVisible = false;
            Frame.Opacity = from;
            _ = FadeAsync(true, from);
        }
        else
        {
            Settle(true);
        }
    }

    /// <summary>Keeps the opacity and the anchor-relative transform on the same clock.</summary>
    /// <param name="closing">Whether the card is leaving.</param>
    /// <param name="from">The visible opacity before the previous motion was cancelled.</param>
    /// <returns>The optional reveal transition.</returns>
    private async Task FadeAsync(bool closing, double from)
    {
        var to = closing ? 0d : 1d;
        if (from == to)
        {
            Settle(closing);
            return;
        }

        using var lifetime = new CancellationTokenSource();
        _reveal = lifetime;
        var duration = this.TryFindResource("TLine", out var value) && value is TimeSpan time ? time : TimeSpan.FromMilliseconds(120);
        var animation = new Animation
        {
            Duration = ScaleFromAnchor ? duration * Math.Abs(to - from) : duration,
            Easing = ScaleFromAnchor ? new SplineEasing(0.16, 1, 0.3, 1) : new CubicEaseOut(),
            Children = { new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, from) } }, new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, to) } } },
        };
        try
        {
            await animation.RunAsync(Frame, lifetime.Token).ConfigureAwait(true);
            if (!lifetime.IsCancellationRequested)
            {
                Settle(closing);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_reveal, lifetime))
            {
                _reveal = null;
            }
        }
    }

    /// <summary>Applies the collapsed pose around the popup's anchor edge.</summary>
    private void UpdateAnchorTransform()
    {
        var hidden = 1 - Frame.Opacity;
        var scale = 1 - (0.015 * hidden);
        _anchorTransform.Matrix = new Matrix(scale, 0, 0, scale, 0, 4 * hidden);
    }

    /// <summary>Publishes the requested state after animation or reduced motion.</summary>
    /// <param name="closing">Whether to remove the popup from the overlay.</param>
    private void Settle(bool closing)
    {
        if (closing)
        {
            Surface.IsOpen = false;
        }

        _hiding = false;
        Frame.Opacity = 1;
    }

    /// <summary>Applies a change in the owning window's effective motion preference.</summary>
    /// <param name="sender">The window's style classes.</param>
    /// <param name="args">The changed classes.</param>
    private void MotionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (_reveal is { IsCancellationRequested: false } reveal && _owner?.Classes.Contains("motion") != true)
        {
            reveal.Cancel();
            Settle(_hiding);
        }
    }

    /// <summary>Leaves enough time to cross the card's pointer bridge.</summary>
    private void ScheduleClose()
    {
        if (_hiding)
        {
            return;
        }

        _leave.Stop();
        _leave.Start();
    }

    /// <summary>Dismisses the current card and restores its trigger's keyboard focus.</summary>
    /// <param name="sender">Owning top level.</param><param name="args">Keyboard input.</param>
    private void OnOwnerKey(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape || !IsOpen)
        {
            return;
        }

        Close();
        _restoringFocus = true;
        try
        {
            Anchor?.Focus();
        }
        finally
        {
            _restoringFocus = false;
        }

        args.Handled = true;
    }

    /// <summary>Closes outside pointer presses without swallowing the action underneath.</summary>
    /// <param name="sender">Owning window.</param><param name="args">The original pointer input.</param>
    private void OnOwnerPointer(object? sender, PointerPressedEventArgs args)
    {
        if (!IsOpen || (args.Source is Visual visual && (ReferenceEquals(visual, Frame) || Frame.IsVisualAncestorOf(visual) || ReferenceEquals(visual, AnchorPresenter) || AnchorPresenter.IsVisualAncestorOf(visual))))
        {
            return;
        }

        Close();
    }

    /// <summary>Releases the owner's routed-event subscription.</summary>
    private void DetachOwner()
    {
        if (_owner is not null)
        {
            _owner.Classes.CollectionChanged -= MotionChanged;
        }

        _owner?.RemoveHandler(KeyDownEvent, OnOwnerKey);
        _owner?.RemoveHandler(PointerPressedEvent, OnOwnerPointer);
        _owner = null;
    }

    /// <summary>Uses typed shadows with the reference palette's alpha ordering.</summary>
    private void UpdateShadow() => Frame.BoxShadow = BoxShadows.Parse(ActualThemeVariant == ThemeVariant.Dark
        ? "0 0 0 1 #6B000000, 0 18 48 -18 #C2000000"
        : "inset 0 1 0 #E6FFFFFF, 0 16 40 -14 #33171E2D, 0 4 12 -8 #24171E2D");
}
