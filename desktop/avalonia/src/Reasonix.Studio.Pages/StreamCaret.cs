using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Reasonix.Studio.Pages;

/// <summary>Draws the streaming mark at the end of selectable text.</summary>
internal sealed class StreamCaret : Control
{
    /// <summary>Defines the mark's color.</summary>
    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<StreamCaret, IBrush?>(nameof(Fill));

    /// <summary>Defines the normalized position in the blink cycle.</summary>
    public static readonly StyledProperty<double> PhaseProperty =
        AvaloniaProperty.Register<StreamCaret, double>(nameof(Phase));

    /// <summary>Initializes static members of the <see cref="StreamCaret"/> class.</summary>
    static StreamCaret() => AffectsRender<StreamCaret>(FillProperty, PhaseProperty);

    /// <summary>Initializes a new instance of the <see cref="StreamCaret"/> class.</summary>
    public StreamCaret() => LayoutUpdated += (_, _) =>
    {
        if (IsEffectivelyVisible)
        {
            InvalidateVisual();
        }
    };

    /// <summary>Gets or sets the mark's color.</summary>
    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>Gets or sets the normalized position in the blink cycle.</summary>
    public double Phase
    {
        get => GetValue(PhaseProperty);
        set => SetValue(PhaseProperty, value);
    }

    /// <summary>Gets or sets the mark's size.</summary>
    public Size MarkSize
    {
        get; set;
    } = new Size(6, 13);

    /// <summary>Gets or sets the last text block of the stream.</summary>
    internal TextBlock? Target
    {
        get; set;
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        if (Phase >= 0.5 || Fill is not { } fill || Target is not { IsEffectivelyVisible: true } text)
        {
            return;
        }

        var layout = text.TextLayout;
        var end = layout.HitTestTextPosition(-1);
        if (layout.TextLines.Count == 0)
        {
            return;
        }

        var line = layout.TextLines[^1];
        var position = new Point(
            text.Padding.Left + end.X + 2,
            text.Padding.Top + end.Y + line.Baseline + 2 - MarkSize.Height);
        if (text.TranslatePoint(position, this) is Point origin)
        {
            context.FillRectangle(fill, new Rect(origin, MarkSize));
        }
    }
}
