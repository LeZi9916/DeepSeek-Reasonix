using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Reasonix.Studio.Controls;

/// <summary>A compact rate curve scaled against its lifetime peak.</summary>
public sealed class StudioSparkline : Control
{
    /// <summary>Defines the curve samples.</summary>
    public static readonly StyledProperty<IReadOnlyList<double>> PointsProperty = AvaloniaProperty.Register<StudioSparkline, IReadOnlyList<double>>(nameof(Points), Array.Empty<double>());

    /// <summary>Defines the lifetime scale ceiling.</summary>
    public static readonly StyledProperty<double> PeakProperty = AvaloniaProperty.Register<StudioSparkline, double>(nameof(Peak), 1);

    /// <summary>Defines the semantic curve ink.</summary>
    public static readonly StyledProperty<IBrush?> StrokeProperty = AvaloniaProperty.Register<StudioSparkline, IBrush?>(nameof(Stroke));

    /// <summary>Defines the translucent area beneath the curve.</summary>
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<StudioSparkline, IBrush?>(nameof(Fill));

    /// <summary>Defines whether the curve belongs to an active generation.</summary>
    public static readonly StyledProperty<bool> IsLiveProperty = AvaloniaProperty.Register<StudioSparkline, bool>(nameof(IsLive));

    /// <summary>Initializes static members of the <see cref="StudioSparkline"/> class.</summary>
    static StudioSparkline() => AffectsRender<StudioSparkline>(PointsProperty, PeakProperty, StrokeProperty, FillProperty, IsLiveProperty);

    /// <summary>Gets or sets recent rate samples.</summary>
    public IReadOnlyList<double> Points
    {
        get => GetValue(PointsProperty); set => SetValue(PointsProperty, value);
    }

    /// <summary>Gets or sets the curve's lifetime peak.</summary>
    public double Peak
    {
        get => GetValue(PeakProperty); set => SetValue(PeakProperty, value);
    }

    /// <summary>Gets or sets its semantic ink.</summary>
    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value);
    }

    /// <summary>Gets or sets the translucent area beneath the curve.</summary>
    public IBrush? Fill
    {
        get => GetValue(FillProperty); set => SetValue(FillProperty, value);
    }

    /// <summary>Gets or sets a value indicating whether the curve belongs to an active generation.</summary>
    public bool IsLive
    {
        get => GetValue(IsLiveProperty); set => SetValue(IsLiveProperty, value);
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Points.Count < 2 || Bounds.Width <= 0 || Bounds.Height <= 3)
        {
            return;
        }

        var width = Bounds.Width;
        var height = Bounds.Height;
        var peak = Math.Max(1, Math.Max(Peak, Points.Max()));
        double Y(double value) => height - 1.5 - (Math.Max(0, value) / peak * (height - 3));
        var line = new StreamGeometry();
        var fill = new StreamGeometry();
        using (var drawing = line.Open())
        {
            drawing.BeginFigure(new Point(0, Y(Points[0])), true);
            for (var i = 1; i < Points.Count; i++)
            {
                drawing.LineTo(new Point(i * width / (Points.Count - 1), Y(Points[i])));
            }

            drawing.EndFigure(false);
        }

        using (var drawing = fill.Open())
        {
            drawing.BeginFigure(new Point(0, height), true);
            for (var i = 0; i < Points.Count; i++)
            {
                drawing.LineTo(new Point(i * width / (Points.Count - 1), Y(Points[i])));
            }

            drawing.LineTo(new Point(width, height));
            drawing.EndFigure(true);
        }

        if (Fill is { } area)
        {
            context.DrawGeometry(area, null, fill);
        }
        else
        {
            using (context.PushOpacity(0.1))
            {
                context.DrawGeometry(Stroke, null, fill);
            }
        }

        context.DrawGeometry(IsLive ? Stroke : null, new Pen(Stroke, 1.1) { LineJoin = PenLineJoin.Round }, line);
        context.DrawEllipse(Stroke, IsLive ? new Pen(Stroke, 1) : null, new Point(width - 0.8, Y(Points[^1])), 1.7, 1.7);
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsLiveProperty)
        {
            PseudoClasses.Set(":live", IsLive);
        }
    }
}
