using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

namespace Reasonix.Studio.Shell.Appearance;

/// <summary>The optional three-band cloud and sunlight effect from an installed theme pack.</summary>
public sealed class ThemeSky : Control
{
    /// <summary>Seven sunlight shafts in the same proportions as the reference backdrop.</summary>
    private static readonly (double X, double Radius, double Angle)[] s_shafts =
    [
        (0.08, 0.04, 7), (0.22, 0.025, 5), (0.38, 0.055, 3), (0.54, 0.02, -1),
        (0.64, 0.07, -3), (0.81, 0.03, -6), (0.91, 0.045, -9),
    ];

    /// <summary>The cloud rendering cadence of the Electron renderer.</summary>
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1d / 15) };

    /// <summary>Cloud puffs, seeded once per control.</summary>
    private readonly Puff[] _clouds;

    /// <summary>Illuminated and shaded clouds and sunlight.</summary>
    private (IBrush Cloud, IBrush Lit, IBrush Sun) _paint;

    /// <summary>Observed window used to suspend an inactive renderer.</summary>
    private Window? _window;

    /// <summary>The active motion preference.</summary>
    private bool _motion;

    /// <summary>Initializes a new instance of the <see cref="ThemeSky"/> class.</summary>
    public ThemeSky()
    {
        var random = new Random(34);
        _clouds = Enumerable.Range(0, 34).Select(i =>
        {
            var band = Math.Min(2, i * 3 / 34);
            var (y, radius, alpha, speed) = band switch
            {
                0 => (0.08, 46, 0.16, 0.045),
                1 => (0.34, 62, 0.22, 0.026),
                _ => (0.6, 84, 0.30, 0.013),
            };
            return new Puff((random.NextDouble() * 600) - 100, (y + (random.NextDouble() * 0.32)) * 225, radius * (0.65 + (random.NextDouble() * 0.7)), alpha * (0.6 + (random.NextDouble() * 0.8)), speed * (0.7 + (random.NextDouble() * 0.6)), random.NextDouble() < 0.3);
        }).ToArray();
        _timer.Tick += (_, _) =>
        {
            foreach (var puff in _clouds)
            {
                puff.Offset = puff.Offset + (4 * puff.Speed) - puff.Radius > 500 ? -puff.Radius - 100 : puff.Offset + (4 * puff.Speed);
            }

            InvalidateVisual();
        };
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty)
            {
                SyncClock();
            }
        };
        AttachedToVisualTree += (_, _) =>
        {
            _window = TopLevel.GetTopLevel(this) as Window;
            if (_window is not null)
            {
                _window.LayoutUpdated += Presence;
                _window.Activated += Presence;
                _window.Deactivated += Presence;
            }

            SyncClock();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (_window is not null)
            {
                _window.LayoutUpdated -= Presence;
                _window.Activated -= Presence;
                _window.Deactivated -= Presence;
                _window = null;
            }

            _timer.Stop();
        };
    }

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_paint.Cloud is null)
        {
            return;
        }

        var width = Bounds.Width;
        var height = Bounds.Height;
        foreach (var puff in _clouds)
        {
            using var alpha = context.PushOpacity(puff.Alpha);
            context.DrawEllipse(puff.Lit ? _paint.Lit : _paint.Cloud, null, new Point(puff.Offset * width / 400, puff.Y * height / 225), puff.Radius * width / 400, puff.Radius * height / 225);
        }

        context.DrawEllipse(_paint.Sun, null, new Point(width * 0.81, height * 0.04), width * 0.36, width * 0.36);
        using var glow = context.PushOpacity(0.35);
        foreach (var (x, radius, angle) in s_shafts)
        {
            using var shaft = context.PushTransform(Matrix.CreateRotation(angle * Math.PI / 180) * Matrix.CreateTranslation(width * x, 0));
            context.DrawEllipse(_paint.Sun, null, new Point(0, 0), width * radius, height);
        }
    }

    /// <summary>Installs the colors and opacities requested by this pack.</summary>
    /// <param name="sky">Pack specification, null to remove the layer.</param>
    internal void Configure(JsonObject? sky)
    {
        IsVisible = sky is not null;
        if (sky is not null)
        {
            var alpha = Math.Clamp((double?)sky["cloudAlpha"] ?? 0.4, 0, 1);
            _paint = (Glow(ColorOf((string?)sky["cloud"], Color.FromRgb(206, 224, 255)), alpha),
                Glow(ColorOf((string?)sky["cloudLit"], Color.FromRgb(216, 166, 88)), alpha),
                Glow(ColorOf((string?)sky["ray"], Color.FromArgb(140, 255, 216, 142)), Math.Clamp((double?)sky["rayAlpha"] ?? 0.85, 0, 1)));
        }

        SyncClock();
        InvalidateVisual();
    }

    /// <summary>Adopts the effective reduced-motion preference.</summary>
    /// <param name="enabled">Whether motion is allowed.</param>
    internal void Move(bool enabled)
    {
        _motion = enabled;
        SyncClock();
    }

    /// <summary>Creates a soft radial cloud or sun.</summary>
    /// <param name="color">Tint.</param>
    /// <param name="alpha">Opacity from the pack.</param>
    /// <returns>The cached paint.</returns>
    private static RadialGradientBrush Glow(Color color, double alpha) => new()
    {
        GradientStops = new GradientStops { new GradientStop(color, 0), new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1) },
        Opacity = alpha,
    };

    /// <summary>Reads the CSS RGB triples or RGBA ray supplied by the kernel.</summary>
    /// <param name="css">Pack color.</param>
    /// <param name="fallback">Studio default.</param>
    /// <returns>The parsed color.</returns>
    private static Color ColorOf(string? css, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(css))
        {
            return fallback;
        }

        var parts = css.Trim().Replace("rgba(", string.Empty, StringComparison.OrdinalIgnoreCase).Replace("rgb(", string.Empty, StringComparison.OrdinalIgnoreCase).TrimEnd(')').Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length is 3 or 4 && byte.TryParse(parts[0], out var r) && byte.TryParse(parts[1], out var g) && byte.TryParse(parts[2], out var b))
        {
            var alpha = parts.Length == 4 && double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? Math.Clamp(value, 0, 1) : 1;
            return Color.FromArgb((byte)Math.Round(alpha * 255), r, g, b);
        }

        return Color.TryParse(css, out var color) ? color : fallback;
    }

    /// <summary>Updates scheduling on native window activation.</summary>
    /// <param name="sender">The window.</param>
    /// <param name="e">The change.</param>
    private void Presence(object? sender, EventArgs e) => SyncClock();

    /// <summary>Runs only for a visible, active window with motion enabled.</summary>
    private void SyncClock() => _timer.IsEnabled = _motion && IsEffectivelyVisible && _window?.IsActive == true;

    /// <summary>One cloud in the reference 400 by 225 drawing space.</summary>
    /// <param name="X">Initial position.</param>
    /// <param name="Y">Band height.</param>
    /// <param name="Radius">Radius.</param>
    /// <param name="Alpha">Opacity.</param>
    /// <param name="Speed">Drift per frame.</param>
    /// <param name="Lit">Whether the cloud catches the light.</param>
    private sealed record Puff(double X, double Y, double Radius, double Alpha, double Speed, bool Lit)
    {
        /// <summary>Gets or sets the moving center of the cloud.</summary>
        public double Offset { get; set; } = X;
    }
}
