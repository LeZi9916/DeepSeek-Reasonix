using Avalonia;
using Avalonia.Controls;
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

    /// <summary>The transition time as set by the reference menu.</summary>
    public static readonly StyledProperty<int> MotionMillisecondsProperty =
        AvaloniaProperty.Register<ComposerPopup, int>(nameof(MotionMilliseconds), 260);

    /// <summary>The mounted popover's input and transition state.</summary>
    private readonly ComposerMenuMotion _motion;

    /// <summary>Initializes a new instance of the <see cref="ComposerPopup"/> class.</summary>
    public ComposerPopup()
    {
        InitializeComponent();
        _motion = new ComposerMenuMotion(this, Surface, Frame);
        _motion.Closed += () => Closed?.Invoke();
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

    /// <summary>Gets or sets the reference duration of the popup's transition.</summary>
    public int MotionMilliseconds
    {
        get => GetValue(MotionMillisecondsProperty);
        set => SetValue(MotionMillisecondsProperty, value);
    }

    /// <summary>Gets a value indicating whether the popup is accepting input.</summary>
    public bool IsOpen => _motion.IsOpen;

    /// <summary>Opens the panel above its trigger and puts keyboard focus on the first choice.</summary>
    /// <param name="placementTarget">An alternate anchor when the pane uses a two-row toolbar.</param>
    /// <param name="paneWidth">The width of the compact pane's content area.</param>
    public void Show(Control? placementTarget = null, double? paneWidth = null)
    {
        if (Anchor is null || Panel is null)
        {
            return;
        }

        Surface.PlacementTarget = placementTarget ?? Anchor;
        Surface.VerticalOffset = -9;
        var viewport = TopLevel.GetTopLevel(this)?.ClientSize ?? new Size(900, 700);
        Frame.Padding = MenuPadding;
        Frame.Width = Math.Min(PreferredWidth, paneWidth ?? Math.Max(220, viewport.Width - 24));
        Panel.Width = Math.Max(0, Frame.Width - MenuPadding.Left - MenuPadding.Right - 2);
        Scroll.MaxHeight = Math.Clamp(viewport.Height - 180, 200, 560);
        Frame.BoxShadow = BoxShadows.Parse(ActualThemeVariant == ThemeVariant.Dark
            ? "0 0 0 1 #59000000, 0 22 60 -22 #D9000000"
            : "inset 0 1 0 #E6FFFFFF, 0 18 44 -18 #33171E2D");
        _motion.Open(Anchor, MotionMilliseconds);
        Dispatcher.UIThread.Post(FocusFirst);
    }

    /// <summary>Closes the popup without changing the session.</summary>
    public void Close() => _motion.Close();

    /// <summary>Dismisses the overlay when its owning conversation is replaced.</summary>
    internal void CloseImmediately() => _motion.Close(immediately: true);

    /// <summary>Focuses the first usable choice after the popup has been laid out.</summary>
    private void FocusFirst()
    {
        if (IsOpen)
        {
            var first = Panel?.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.IsEffectivelyVisible && button.IsEnabled);
            first?.Focus();
        }
    }
}
