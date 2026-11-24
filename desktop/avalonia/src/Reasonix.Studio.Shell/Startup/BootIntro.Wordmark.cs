using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using System;
using System.Linq;
using static Reasonix.Studio.Shell.Startup.BootSky;

namespace Reasonix.Studio.Shell.Startup;

internal sealed partial class BootIntro
{
    /// <summary>The curve that pulls the shards together.</summary>
    private static readonly SplineEasing s_slam = new(0.1, 0.85, 0.2, 1);

    /// <summary>The decay curve of the R’s flash.</summary>
    private static readonly SplineEasing s_glow = new(0, 0, 0.58, 1);

    /// <summary>Interpolates two opaque colors in channel space.</summary>
    /// <param name="a">The start value.</param>
    /// <param name="b">The end value.</param>
    /// <param name="time">The elapsed time or interpolation fraction.</param>
    /// <returns>The interpolated color.</returns>
    private static Color Blend(Color a, Color b, double time) =>
        Color.FromRgb(
            (byte)Math.Round(Mix(a.R, b.R, time)),
            (byte)Math.Round(Mix(a.G, b.G, time)),
            (byte)Math.Round(Mix(a.B, b.B, time)));

    /// <summary>Converts a heading to a unit vector.</summary>
    /// <param name="angle">The angle in radians.</param>
    /// <returns>A direction vector of length one.</returns>
    private static Vector Direction(double angle) => new(Math.Cos(angle), Math.Sin(angle));

    /// <summary>Paints the unfolding letters, gold R and passing glint.</summary>
    /// <param name="context">The startup drawing context.</param>
    /// <param name="origin">Top-left position of the wordmark.</param>
    /// <param name="scale">Viewport scale derived from the SVG view box.</param>
    /// <param name="shift">The horizontal displacement in wordmark units.</param>
    /// <param name="pose">Milliseconds into the opening sequence.</param>
    /// <param name="exit">Milliseconds since the page started taking over.</param>
    /// <param name="dark">Whether the underlying page uses the dark theme.</param>
    private void DrawWordmark(DrawingContext context, Point origin, double scale, double shift, double pose, double exit, bool dark)
    {
        using var position = context.PushTransform(Matrix.CreateTranslation(-262 + shift, -70) *
            Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(origin.X, origin.Y));
        var mark = _glyphs!;
        var departure = !double.IsNaN(exit);
        var glyphInk = s_cream;
        var letterAlpha = 1d;
        var gold = s_gold;
        if (departure)
        {
            var phase = Unit(exit / 900);
            if (!_calm)
            {
                letterAlpha = phase <= 0.4 ? Mix(1, 0.15, phase / 0.4) : Mix(0.15, 1, Unit((phase - 0.56) / 0.44));
            }

            glyphInk = phase < 0.56 || dark ? s_cream : Color.Parse("#342E22");
            gold = Blend(s_gold, Color.Parse(dark ? "#DDA144" : "#A6680C"), s_settle.Ease(phase));
        }

        if (_calm)
        {
            using var calm = context.PushOpacity(Unit(_time / 300));
            context.DrawGeometry(new SolidColorBrush(gold), null, mark[0]);
            for (var i = 1; i < mark.Length; i++)
            {
                context.DrawGeometry(new SolidColorBrush(glyphInk), null, mark[i]);
            }

            return;
        }

        if (pose is >= Hit and < Lock + 40)
        {
            DrawShards(context, mark[0], pose, gold);
        }

        if (pose >= Lock - 20)
        {
            var alpha = Unit((pose - Lock + 20) / 40);
            var heat = departure ? 0 : 1 - s_glow.Ease(Unit((pose - Lock + 20) / 760));
            using var found = context.PushOpacity(alpha);
            if (heat > 0)
            {
                var effect = new DropShadowEffect { BlurRadius = 10 / scale, Color = Color.Parse("#FFCC80"), OffsetX = 0, OffsetY = 0, Opacity = 0.9 * heat };
                using var halo = context.PushEffect(effect, mark[0].Bounds.Inflate(30 / scale));
                var brightness = 1 + (1.3 * heat);
                var lit = Color.FromRgb(
                    (byte)Math.Min(255, Math.Round(gold.R * brightness)),
                    (byte)Math.Min(255, Math.Round(gold.G * brightness)),
                    (byte)Math.Min(255, Math.Round(gold.B * brightness)));
                context.DrawGeometry(new SolidColorBrush(lit), null, mark[0]);
            }
            else
            {
                context.DrawGeometry(new SolidColorBrush(gold), null, mark[0]);
            }
        }

        for (var i = 1; i < mark.Length; i++)
        {
            var age = Unit((pose - Unfold - ((i - 1) * 45)) / 480);
            if (age <= 0)
            {
                continue;
            }

            var slide = (i == 1 ? mark[0].Bounds.Center.X : mark[i - 1].Bounds.X) - mark[i].Bounds.X;
            using var letter = context.PushTransform(Matrix.CreateTranslation(slide * (1 - s_out.Ease(age)), 0));
            using var opacity = context.PushOpacity(letterAlpha * s_out.Ease(Unit(age / 0.45)));
            context.DrawGeometry(new SolidColorBrush(glyphInk), null, mark[i]);
        }

        if (!double.IsNaN(_heldAt))
        {
            var glint = (_time - _heldAt) % 3200;
            var opacity = departure ? 1 - s_ease.Ease(Unit(exit / 300)) : 1;
            if (glint < 1984 && opacity > 0)
            {
                var x = Mix(-300, 1500, glint / 1984);
                var colors = new GradientStops
                {
                    new(Color.FromArgb(0, 255, 246, 222), 0),
                    new(Ink(255, 246, 222, opacity * 0.85).Color, 0.5),
                    new(Color.FromArgb(0, 255, 246, 222), 1),
                };
                foreach (var glyph in mark)
                {
                    var shine = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(x - glyph.Bounds.X, 60 - glyph.Bounds.Y, RelativeUnit.Absolute),
                        EndPoint = new RelativePoint(x + 220 - glyph.Bounds.X, 330 - glyph.Bounds.Y, RelativeUnit.Absolute),
                        GradientStops = colors,
                    };
                    context.DrawGeometry(shine, null, glyph);
                }
            }
        }
    }

    /// <summary>Cuts the R into seven offset wedges for this launch.</summary>
    /// <param name="glyph">The source R outline or its bounds.</param>
    /// <returns>The clipped pieces.</returns>
    private Shard[] CreateShards(Rect glyph)
    {
        const int Count = 7;
        var pivot = new Point(
            glyph.X + (glyph.Width * (0.42 + (_random.NextDouble() * 0.16))),
            glyph.Y + (glyph.Height * (0.4 + (_random.NextDouble() * 0.2))));
        var reach = new Vector(glyph.Width, glyph.Height).Length * 1.5;
        var span = Math.Tau / Count;
        var start = _random.NextDouble() * span;
        var cuts = Enumerable.Range(0, Count).Select(i => start + ((i + 0.25 + (_random.NextDouble() * 0.5)) * span)).ToArray();
        return Enumerable.Range(0, Count).Select(i =>
        {
            var from = cuts[i];
            var to = i == Count - 1 ? cuts[0] + Math.Tau : cuts[i + 1];
            var middle = (from + to) / 2;
            var clip = new StreamGeometry();
            using (var wedge = clip.Open())
            {
                wedge.BeginFigure(pivot, true);
                wedge.LineTo(pivot + (Direction(from) * reach));
                wedge.LineTo(pivot + (Direction(middle) * reach));
                wedge.LineTo(pivot + (Direction(to) * reach));
                wedge.EndFigure(true);
            }

            var offset = Direction(middle) * (glyph.Width * (0.55 + (_random.NextDouble() * 0.5)));
            return new Shard(clip, offset, (_random.NextDouble() - 0.5) * Math.PI * 24 / 180);
        }).ToArray();
    }

    /// <summary>Moves the clipped copies back into the R shape.</summary>
    /// <param name="context">The startup drawing context.</param>
    /// <param name="glyph">The source R outline or its bounds.</param>
    /// <param name="pose">Milliseconds into the opening sequence.</param>
    /// <param name="gold">The paint for the R.</param>
    private void DrawShards(DrawingContext context, Geometry glyph, double pose, Color gold)
    {
        _shards ??= CreateShards(glyph.Bounds);
        var center = glyph.Bounds.Center;
        var age = Unit((pose - Hit) / (Lock - Hit));
        var remaining = 1 - s_slam.Ease(age);
        var alpha = s_slam.Ease(Unit(age / 0.3)) * (1 - Unit((pose - Lock) / 40));
        using var opacity = context.PushOpacity(alpha);
        foreach (var piece in _shards)
        {
            var transform = Matrix.CreateTranslation(-center.X, -center.Y) * Matrix.CreateScale(1 + (0.5 * remaining), 1 + (0.5 * remaining)) *
                Matrix.CreateRotation(piece.Rotation * remaining) *
                Matrix.CreateTranslation(center.X + (piece.Distance.X * remaining), center.Y + (piece.Distance.Y * remaining));
            using var offset = context.PushTransform(transform);
            using var clip = context.PushGeometryClip(piece.Clip);
            context.DrawGeometry(new SolidColorBrush(gold), null, glyph);
        }
    }

    /// <summary>One clipped piece of the R.</summary>
    /// <param name="Clip">The wedge enclosing the piece.</param>
    /// <param name="Distance">The outward displacement.</param>
    /// <param name="Rotation">The initial rotation.</param>
    private readonly record struct Shard(Geometry Clip, Vector Distance, double Rotation);
}
