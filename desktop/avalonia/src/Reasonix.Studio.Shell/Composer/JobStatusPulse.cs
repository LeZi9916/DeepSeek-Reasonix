using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Reasonix.Studio.Shell.Composer;

/// <summary>Draws the background job dot and its expanding ring without moving the rail.</summary>
internal sealed class JobStatusPulse : Control
{
    /// <summary>Defines the pulse's interpolation between its widest and narrowest points.</summary>
    public static readonly StyledProperty<double> PhaseProperty =
        AvaloniaProperty.Register<JobStatusPulse, double>(nameof(Phase), 1);

    /// <summary>Defines the stationary dot's brush.</summary>
    public static readonly StyledProperty<IBrush?> InkProperty =
        AvaloniaProperty.Register<JobStatusPulse, IBrush?>(nameof(Ink));

    /// <summary>Defines the brush used by the expanding ring.</summary>
    public static readonly StyledProperty<IBrush?> RingProperty =
        AvaloniaProperty.Register<JobStatusPulse, IBrush?>(nameof(Ring));

    /// <summary>Initializes static members of the <see cref="JobStatusPulse"/> class.</summary>
    static JobStatusPulse() => AffectsRender<JobStatusPulse>(PhaseProperty, InkProperty, RingProperty);

    /// <summary>Gets or sets the eased progress of the expansion.</summary>
    public double Phase
    {
        get => GetValue(PhaseProperty);
        set => SetValue(PhaseProperty, value);
    }

    /// <summary>Gets or sets the stationary dot's brush.</summary>
    public IBrush? Ink
    {
        get => GetValue(InkProperty);
        set => SetValue(InkProperty, value);
    }

    /// <summary>Gets or sets the brush used by the expanding ring.</summary>
    public IBrush? Ring
    {
        get => GetValue(RingProperty);
        set => SetValue(RingProperty, value);
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        const double DotRadius = 3;
        if (Ring is { } ring && Phase is > 0 and < 1)
        {
            using var opacity = context.PushOpacity(1 - Phase);
            var radius = DotRadius + (5 * Phase);
            context.DrawEllipse(ring, null, center, radius, radius);
        }

        context.DrawEllipse(Ink, null, center, DotRadius, DotRadius);
    }
}
