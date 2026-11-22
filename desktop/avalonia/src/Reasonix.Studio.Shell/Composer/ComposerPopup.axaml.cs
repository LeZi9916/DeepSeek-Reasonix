using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Linq;

namespace Reasonix.Studio.Shell.Composer;

/// <summary>An anchored popup host for the composer's small choice panels.</summary>
public sealed partial class ComposerPopup : UserControl
{
    /// <summary>Defines the trigger the popup is anchored to.</summary>
    public static readonly StyledProperty<Control?> AnchorProperty =
        AvaloniaProperty.Register<ComposerPopup, Control?>(nameof(Anchor));

    /// <summary>Defines the panel the popup presents.</summary>
    public static readonly StyledProperty<Control?> PanelProperty =
        AvaloniaProperty.Register<ComposerPopup, Control?>(nameof(Panel));

    /// <summary>Defines the panel width before viewport clamping.</summary>
    public static readonly StyledProperty<double> PreferredWidthProperty =
        AvaloniaProperty.Register<ComposerPopup, double>(nameof(PreferredWidth), 300);

    /// <summary>Defines the padding inside the panel frame.</summary>
    public static readonly StyledProperty<Thickness> MenuPaddingProperty =
        AvaloniaProperty.Register<ComposerPopup, Thickness>(nameof(MenuPadding), new Thickness(8));

    /// <summary>The window that owns the Escape handler.</summary>
    private TopLevel? _owner;

    /// <summary>Initializes a new instance of the <see cref="ComposerPopup"/> class.</summary>
    public ComposerPopup()
    {
        InitializeComponent();
        Surface.Closed += (_, _) =>
        {
            Anchor?.Classes.Remove("open");
            Closed?.Invoke();
        };
        AttachedToVisualTree += (_, _) =>
        {
            _owner = TopLevel.GetTopLevel(this);
            _owner?.AddHandler(KeyDownEvent, OnOwnerKey, RoutingStrategies.Tunnel);
        };
        DetachedFromVisualTree += (_, _) =>
        {
            Close();
            _owner?.RemoveHandler(KeyDownEvent, OnOwnerKey);
            _owner = null;
        };
    }

    /// <summary>Raised after the popup closed, including light dismissal.</summary>
    public event Action? Closed;

    /// <summary>Gets or sets the trigger the popup is anchored to.</summary>
    public Control? Anchor
    {
        get => GetValue(AnchorProperty);
        set => SetValue(AnchorProperty, value);
    }

    /// <summary>Gets or sets the panel the popup presents.</summary>
    public Control? Panel
    {
        get => GetValue(PanelProperty);
        set => SetValue(PanelProperty, value);
    }

    /// <summary>Gets or sets the panel width before viewport clamping.</summary>
    public double PreferredWidth
    {
        get => GetValue(PreferredWidthProperty);
        set => SetValue(PreferredWidthProperty, value);
    }

    /// <summary>Gets or sets the padding inside the panel frame.</summary>
    public Thickness MenuPadding
    {
        get => GetValue(MenuPaddingProperty);
        set => SetValue(MenuPaddingProperty, value);
    }

    /// <summary>Gets a value indicating whether the popup is visible.</summary>
    public bool IsOpen => Surface.IsOpen;

    /// <summary>Opens the panel above its trigger and puts keyboard focus on the first choice.</summary>
    public void Show()
    {
        if (Anchor is null || Panel is null)
        {
            return;
        }

        Surface.PlacementTarget = Anchor;
        Surface.VerticalOffset = -13;
        var viewport = TopLevel.GetTopLevel(this)?.ClientSize ?? new Size(900, 700);
        Frame.Padding = MenuPadding;
        Frame.Width = Math.Min(PreferredWidth, Math.Max(220, viewport.Width - 24));
        Panel.Width = Math.Max(0, Frame.Width - MenuPadding.Left - MenuPadding.Right - 2);
        Scroll.MaxHeight = Math.Clamp(viewport.Height - 180, 200, 560);
        Frame.BoxShadow = BoxShadows.Parse(ActualThemeVariant == ThemeVariant.Dark
            ? "0 0 0 1 #59000000, 0 22 60 -22 #D9000000"
            : "inset 0 1 0 #E6FFFFFF, 0 18 44 -18 #33171E2D");
        Anchor.Classes.Add("open");
        Surface.IsOpen = true;
        Dispatcher.UIThread.Post(FocusFirst);
    }

    /// <summary>Closes the popup without changing the session.</summary>
    public void Close() => Surface.IsOpen = false;

    /// <summary>Focuses the first usable choice after the popup has been laid out.</summary>
    private void FocusFirst()
    {
        var first = Panel?.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.IsEffectivelyVisible && button.IsEnabled);
        first?.Focus();
    }

    /// <summary>Closes on Escape and returns focus to the trigger.</summary>
    /// <param name="sender">Owning top level.</param>
    /// <param name="args">Keyboard input.</param>
    private void OnOwnerKey(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape || !IsOpen)
        {
            return;
        }

        Close();
        Anchor?.Focus();
        args.Handled = true;
    }
}
