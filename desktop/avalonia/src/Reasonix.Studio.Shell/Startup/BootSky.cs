using Avalonia;
using Avalonia.Media;
using System;
using System.Linq;

namespace Reasonix.Studio.Shell.Startup;

/// <summary>Renders the sky, meteor and impact behind the startup wordmark.</summary>
internal sealed class BootSky
{
    /// <summary>Time when the meteor starts accelerating.</summary>
    internal const double Push = 380;

    /// <summary>Time of the meteor’s impact.</summary>
    internal const double Hit = 620;

    /// <summary>Time when the sky stops moving.</summary>
    internal const double Quiet = 1500;

    /// <summary>Reuses the star inks at the exact alpha values used by the animation.</summary>
    private static readonly IBrush[] s_starInk = Enumerable.Range(0, 256)
        .Select(alpha => new SolidColorBrush(Color.FromArgb((byte)alpha, 226, 224, 240)).ToImmutable())
        .ToArray();

    /// <summary>The positions and brightness of the stars for this launch.</summary>
    private readonly Star[] _stars;

    /// <summary>The impact particles and their initial motion.</summary>
    private readonly Ember[] _embers;

    /// <summary>Initializes a new instance of the <see cref="BootSky"/> class from randomly placed stars and embers.</summary>
    /// <param name="random">Random source for positions and velocities.</param>
    internal BootSky(Random random)
    {
        _stars = Enumerable.Range(0, 240).Select(_ => new Star(
            random.NextDouble(),
            random.NextDouble(),
            random.NextDouble() < 0.1 ? 1.4 : 0.6 + (random.NextDouble() * 0.5),
            0.3 + (random.NextDouble() * 0.55),
            random.NextDouble() * Math.Tau)).ToArray();
        _embers = Enumerable.Range(0, 18).Select(_ =>
        {
            var angle = (-Math.PI / 2) + ((random.NextDouble() - 0.5) * Math.PI * 1.5);
            var speed = 0.25 + (random.NextDouble() * 0.55);
            return new Ember(
                new Vector((random.NextDouble() - 0.5) * 30, (random.NextDouble() - 0.5) * 20),
                new Vector(Math.Cos(angle) * speed, Math.Sin(angle) * speed),
                450 + (random.NextDouble() * 550));
        }).ToArray();
    }

    /// <summary>Constrains a value to the unit interval.</summary>
    /// <param name="value">The value to constrain.</param>
    /// <returns>The clamped value.</returns>
    internal static double Unit(double value) => Math.Clamp(value, 0, 1);

    /// <summary>Interpolates between two values.</summary>
    /// <param name="a">The start value.</param>
    /// <param name="b">The end value.</param>
    /// <param name="t">The interpolation fraction.</param>
    /// <returns>The interpolated value.</returns>
    internal static double Mix(double a, double b, double t) => a + ((b - a) * t);

    /// <summary>Creates a paint from its color channels and opacity.</summary>
    /// <param name="red">The red channel.</param>
    /// <param name="green">The green channel.</param>
    /// <param name="blue">The blue channel.</param>
    /// <param name="opacity">The requested opacity.</param>
    /// <returns>The requested paint.</returns>
    internal static SolidColorBrush Ink(byte red, byte green, byte blue, double opacity = 1) =>
        new(Color.FromArgb((byte)Math.Round(Unit(opacity) * 255), red, green, blue));

    /// <summary>Renders the sky and the current part of the meteor sequence.</summary>
    /// <param name="context">The startup drawing context.</param>
    /// <param name="size">The viewport size.</param>
    /// <param name="impact">The position of the impact.</param>
    /// <param name="time">The elapsed time.</param>
    internal void Draw(DrawingContext context, Size size, Point impact, double time)
    {
        DrawStars(context, size, impact, time);
        if (time < Hit)
        {
            DrawMeteor(context, size, impact, time);
        }
        else
        {
            DrawImpact(context, size, impact, time - Hit);
        }
    }

    /// <summary>Computes the stellar expansion during the meteor’s acceleration.</summary>
    /// <param name="time">The elapsed time.</param>
    /// <returns>The scale around the impact.</returns>
    private static double Zoom(double time) => time is >= Push and < Hit ? 1 + (5 * Math.Pow((time - Push) / (Hit - Push), 2.4)) : 1;

    /// <summary>Draws the head, halo and trail before impact.</summary>
    /// <param name="context">The startup drawing context.</param>
    /// <param name="size">The viewport size.</param>
    /// <param name="impact">The position of the impact.</param>
    /// <param name="time">The elapsed time.</param>
    private static void DrawMeteor(DrawingContext context, Size size, Point impact, double time)
    {
        var from = new Point(size.Width * 1.02, size.Height * 0.1);
        var bend = new Point(Mix(from.X, impact.X, 0.62), Mix(from.Y, impact.Y, 0.5));
        var course = new Vector(bend.X - from.X, bend.Y - from.Y);
        var direction = course / course.Length;
        var normal = new Vector(-direction.Y, direction.X);
        var rush = Math.Pow(Unit((time - Push) / (Hit - Push)), 2.4);
        var head = time < Push ? from + ((bend - from) * (time / Push)) : bend + ((impact - bend) * rush);
        var core = 1.3 + (9 * rush);
        var tail = head - (direction * (size.Width * 0.3 * Math.Min(1, time / 120) * (1 - (0.55 * rush))));
        DrawTail(context, head, tail, normal * (core * 3.2), 0.16);
        DrawTail(context, head, tail, normal * (core * 0.8), 1);
        DrawGlow(context, head, core * 5, core * 5, new GradientStops
        {
            new(Color.FromRgb(255, 255, 250), 0),
            new(Color.FromArgb(179, 255, 228, 170), 0.2),
            new(Color.FromArgb(0, 240, 170, 70), 1),
        });
    }

    /// <summary>Draws one tapered layer of the trail.</summary>
    /// <param name="context">The startup drawing context.</param>
    /// <param name="head">The bright end of the trail.</param>
    /// <param name="tail">The trailing end.</param>
    /// <param name="wing">Half width and direction of the trail.</param>
    /// <param name="opacity">The requested opacity.</param>
    private static void DrawTail(DrawingContext context, Point head, Point tail, Vector wing, double opacity)
    {
        var course = new Vector(tail.X - head.X, tail.Y - head.Y);
        var width = wing.Length;
        var geometry = new StreamGeometry();
        using (var line = geometry.Open())
        {
            line.BeginFigure(new Point(0, -width), true);
            line.LineTo(new Point(course.Length, 0));
            line.LineTo(new Point(0, width));
            line.EndFigure(true);
        }

        using var aim = context.PushTransform(Matrix.CreateRotation(Math.Atan2(course.Y, course.X)) * Matrix.CreateTranslation(head.X, head.Y));
        var paint = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, width, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(course.Length, width, RelativeUnit.Absolute),
            GradientStops = new GradientStops
            {
                new(Ink(255, 248, 230, opacity).Color, 0),
                new(Ink(255, 214, 130, opacity * 0.8).Color, 0.1),
                new(Ink(214, 140, 48, opacity * 0.28).Color, 0.45),
                new(Color.FromArgb(0, 180, 100, 30), 1),
            },
        };
        context.DrawGeometry(paint, null, geometry);
    }

    /// <summary>Fills a circular or elliptical glow.</summary>
    /// <param name="context">The startup drawing context.</param>
    /// <param name="at">Center of the glow.</param>
    /// <param name="rx">Horizontal radius.</param>
    /// <param name="ry">Vertical radius.</param>
    /// <param name="stops">Colors along the glow.</param>
    private static void DrawGlow(DrawingContext context, Point at, double rx, double ry, GradientStops stops) =>
        context.DrawEllipse(new RadialGradientBrush { GradientStops = stops }, null, at, rx, ry);

    /// <summary>Draws individual stars or their trails while the viewpoint moves.</summary>
    /// <param name="context">The startup drawing context.</param>
    /// <param name="size">The viewport size.</param>
    /// <param name="impact">The position of the impact.</param>
    /// <param name="time">The elapsed time.</param>
    private void DrawStars(DrawingContext context, Size size, Point impact, double time)
    {
        var viewport = new Rect(size).Inflate(2);
        var zoom = Zoom(time);
        var previous = Zoom(time - 18);
        var dim = time >= Hit ? 0.45 + (0.2 * Unit((time - Hit) / 800)) : 1;
        foreach (var star in _stars)
        {
            var distance = new Point(star.X * size.Width, star.Y * size.Height) - impact;
            var at = impact + (distance * zoom);
            var tail = zoom > 1.03 ? impact + (distance * previous) : at;
            if ((at.X < viewport.X && tail.X < viewport.X) || (at.X > viewport.Right && tail.X > viewport.Right)
                || (at.Y < viewport.Y && tail.Y < viewport.Y) || (at.Y > viewport.Bottom && tail.Y > viewport.Bottom))
            {
                continue;
            }

            var brightness = star.Alpha * dim * (0.8 + (0.2 * Math.Sin((time / 420) + star.Wave)));
            if (zoom > 1.03)
            {
                context.DrawLine(new Pen(Ink(236, 228, 214, brightness), star.Size, lineCap: PenLineCap.Round), tail, at);
            }
            else
            {
                context.DrawEllipse(s_starInk[(int)Math.Round(Unit(brightness) * 255)], null, at, star.Size / 2, star.Size / 2);
            }
        }
    }

    /// <summary>Draws the flash, light streak and falling embers.</summary>
    /// <param name="context">The startup drawing context.</param>
    /// <param name="size">The viewport size.</param>
    /// <param name="impact">The position of the impact.</param>
    /// <param name="time">The elapsed time.</param>
    private void DrawImpact(DrawingContext context, Size size, Point impact, double time)
    {
        var bloom = Unit(time / 420);
        if (bloom < 1)
        {
            var radius = Math.Min(size.Width, size.Height) * (0.22 + (0.12 * bloom));
            var light = 0.5 * Math.Pow(1 - bloom, 2);
            DrawGlow(context, impact, radius, radius, new GradientStops
            {
                new(Ink(255, 220, 160, light).Color, 0),
                new(Ink(255, 208, 140, light * 0.5).Color, 0.2),
                new(Ink(255, 196, 120, light * 0.14).Color, 0.5),
                new(Color.FromArgb(0, 255, 190, 110), 1),
            });
        }

        var streak = Unit(time / 620);
        if (streak < 1)
        {
            var reach = size.Width * (0.28 + (0.2 * streak));
            var light = Math.Pow(1 - streak, 1.6);
            DrawGlow(context, impact, reach, reach * 0.014, new GradientStops
            {
                new(Ink(255, 236, 200, 0.9 * light).Color, 0),
                new(Ink(255, 196, 110, 0.35 * light).Color, 0.35),
                new(Color.FromArgb(0, 255, 180, 90), 1),
            });
        }

        var drag = Math.Pow(0.985, time / (1000.0 / 60));
        var travel = (1 - drag) * (1000.0 / 60) / 0.015;
        foreach (var ember in _embers)
        {
            if (ember.Duration <= time)
            {
                continue;
            }

            var at = impact + ember.Start + (ember.Velocity * travel) + new Vector(0, 0.00055 * time * time);
            var velocity = (ember.Velocity * drag) + new Vector(0, 0.0011 * time);
            var fade = 0.8 * Unit((ember.Duration - time) / 500);
            context.DrawLine(new Pen(Ink(255, (byte)(170 + (60 * fade)), (byte)(90 + (60 * fade)), fade), 1.1, lineCap: PenLineCap.Round), at, at - (velocity * 12));
        }
    }

    /// <summary>A point in the launch sky.</summary>
    /// <param name="X">Horizontal position as a fraction of width.</param>
    /// <param name="Y">Vertical position as a fraction of height.</param>
    /// <param name="Size">Diameter of the star.</param>
    /// <param name="Alpha">Base opacity.</param>
    /// <param name="Wave">Starting phase of the star pulse.</param>
    private readonly record struct Star(double X, double Y, double Size, double Alpha, double Wave);

    /// <summary>A particle released from the impact.</summary>
    /// <param name="Start">Position relative to the impact.</param>
    /// <param name="Velocity">The velocity at impact.</param>
    /// <param name="Duration">Time until the ember fades.</param>
    private readonly record struct Ember(Vector Start, Vector Velocity, double Duration);
}
