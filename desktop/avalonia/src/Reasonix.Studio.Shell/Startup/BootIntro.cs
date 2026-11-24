using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using static Reasonix.Studio.Shell.Startup.BootSky;

namespace Reasonix.Studio.Shell.Startup;

/// <summary>Draws the original startup sequence above a native window.</summary>
internal sealed partial class BootIntro : Control
{
    /// <summary>Time at which the other letters unfold.</summary>
    private const double Unfold = 980;

    /// <summary>Time at which every letter has settled.</summary>
    private const double Hold = 1730;

    /// <summary>Time of the impact against the complete R.</summary>
    private const double Lock = 860;

    /// <summary>The reference ease-out curve.</summary>
    private static readonly SplineEasing s_out = new(0.16, 1, 0.3, 1);

    /// <summary>The standard CSS ease curve.</summary>
    private static readonly SplineEasing s_ease = new(0.25, 0.1, 0.25, 1);

    /// <summary>The curve that introduces the landing palette.</summary>
    private static readonly SplineEasing s_settle = new(0.45, 0, 0.55, 1);

    /// <summary>The curve for the wordmark’s final enlargement.</summary>
    private static readonly SplineEasing s_scale = new(0.4, 0, 0.2, 1);

    /// <summary>The startup background in both themes.</summary>
    private static readonly IBrush s_night = new RadialGradientBrush
    {
        Center = new RelativePoint(0.5, 0.46, RelativeUnit.Relative),
        GradientOrigin = new RelativePoint(0.5, 0.46, RelativeUnit.Relative),
        RadiusX = new RelativeScalar(1.2, RelativeUnit.Relative),
        RadiusY = new RelativeScalar(0.9, RelativeUnit.Relative),
        GradientStops = new GradientStops { new(Color.Parse("#12141D"), 0), new(Color.Parse("#07080C"), 0.64) },
    }.ToImmutable();

    /// <summary>The neutral startup ink.</summary>
    private static readonly Color s_cream = Color.Parse("#ECE6DA");

    /// <summary>The startup gold accent.</summary>
    private static readonly Color s_gold = Color.Parse("#E9B456");

    /// <summary>The caption and outline ink.</summary>
    private static readonly IBrush s_dim = Brush.Parse("#8A857B");

    /// <summary>Measures elapsed time independently of paint frequency.</summary>
    private readonly Stopwatch _clock = new();

    /// <summary>Provides per-launch differences in the sky and the shattered mark.</summary>
    private readonly Random _random = new();

    /// <summary>The meteor, stars and embers for this launch.</summary>
    private BootSky? _sky;

    /// <summary>The actual grouped Reasonix wordmark outlines.</summary>
    private Geometry[]? _glyphs;

    /// <summary>The wedges fitted to the first glyph.</summary>
    private Shard[]? _shards;

    /// <summary>Whether the operating system requested reduced motion.</summary>
    private bool _calm;

    /// <summary>Whether the first application view is ready to uncover.</summary>
    private bool _ready;

    /// <summary>The last startup milestone reported by the host.</summary>
    private int _progress = 1;

    /// <summary>Elapsed time when the wordmark finished its entrance.</summary>
    private double _heldAt = double.NaN;

    /// <summary>Elapsed time when the landing began.</summary>
    private double _leavingAt = double.NaN;

    /// <summary>Elapsed time when the user skipped the entrance.</summary>
    private double _skipAt = double.NaN;

    /// <summary>Elapsed time of the most recent drawn frame.</summary>
    private double _time;

    /// <summary>Caption for a delayed kernel connection.</summary>
    private string _connecting = string.Empty;

    /// <summary>Caption for a delayed workspace read.</summary>
    private string _preparing = string.Empty;

    /// <summary>Occurs when the first page can begin its arrival.</summary>
    internal event EventHandler? Leaving;

    /// <summary>Gets the playhead of the wordmark entrance.</summary>
    internal double EntranceTime => double.IsNaN(_heldAt) ? _time : Hold;

    /// <summary>Gets the departure playhead once the page starts to take over.</summary>
    internal double DepartureTime => _time - _leavingAt;

    /// <summary>Gets a value indicating whether the entrance can be skipped.</summary>
    internal bool CanSkip => IsVisible && _clock.IsRunning && !_calm && double.IsNaN(_heldAt);

    /// <summary>Gets a value indicating whether to animate the arriving page.</summary>
    internal bool AllowsMotion => !_calm;

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        var overlay = new Rect(size);
        var exit = DepartureTime;
        var leaving = !double.IsNaN(exit);
        var fade = leaving ? 1 - s_ease.Ease(Unit(_calm ? exit / 300 : (exit - 1050) / 500)) : 1;
        using var cover = context.PushOpacity(fade);
        context.FillRectangle(s_night, overlay);
        if (!_clock.IsRunning)
        {
            return;
        }

        _glyphs ??= Enumerable.Range(0, 8).Select(i => (Geometry)this.FindResource($"Boot.Glyph{i}")!).ToArray();
        var wordWidth = Math.Min(480, size.Width * 0.64);
        var scale = wordWidth / 1178;
        var wordHeight = 252 * scale;
        var wordOrigin = new Point((size.Width - wordWidth) / 2, (size.Height * 0.48) - ((wordHeight + 68.5) / 2));
        var impact = new Point(size.Width / 2, wordOrigin.Y + ((_glyphs[0].Bounds.Center.Y - 70) * scale));
        if (_sky is not null)
        {
            var skyAlpha = double.IsNaN(_skipAt) ? 1 : 1 - s_ease.Ease(Unit((_time - _skipAt) / 300));
            using var opacity = context.PushOpacity(skyAlpha);
            _sky.Draw(context, size, impact, Math.Min(double.IsNaN(_skipAt) ? _time : _skipAt, Quiet));
        }

        var dark = ActualThemeVariant == ThemeVariant.Dark;
        if (leaving)
        {
            var ground = s_settle.Ease(Unit(exit / 900));
            using var tint = context.PushOpacity(ground);
            context.FillRectangle(dark ? Brush.Parse("#060A10") : Brush.Parse("#F4F1EA"), overlay);
        }

        var center = new Point(size.Width / 2, size.Height * 0.48);
        var pose = EntranceTime;
        var shift = (851 - _glyphs[0].Bounds.Center.X) * (1 - s_out.Ease(Unit((pose - Unfold) / 650)));
        var impactScale = pose < 840 ? 1 : 1 + (0.045 * (1 - s_out.Ease(Unit((pose - 840) / 420))));
        var magnification = _calm ? 1 : leaving ? 1 + (0.03 * s_scale.Ease(Unit(exit / 1400))) : impactScale;
        using var wordFade = context.PushOpacity(_calm || !leaving ? 1 : 1 - s_ease.Ease(Unit((exit - 900) / 500)));
        using var grow = context.PushTransform(Matrix.CreateTranslation(-center.X, -center.Y) *
            Matrix.CreateScale(magnification, magnification) * Matrix.CreateTranslation(center.X, center.Y));
        DrawWordmark(context, wordOrigin, scale, _calm ? 0 : shift, pose, exit, dark);
        DrawProgress(context, size.Width / 2, wordOrigin.Y + wordHeight, exit);
    }

    /// <summary>Starts the window’s intro on its animation clock.</summary>
    /// <param name="motion">Whether motion is permitted.</param>
    /// <param name="english">Whether to draw the English status caption.</param>
    internal void Begin(bool motion, bool english)
    {
        if (_clock.IsRunning || !IsVisible || TopLevel.GetTopLevel(this) is not { } window)
        {
            return;
        }

        _connecting = english ? "Connecting to the kernel…" : "正在连接内核…";
        _preparing = english ? "Preparing the workspace…" : "正在准备工作区…";
        _calm = !motion;
        if (motion)
        {
            _sky = new BootSky(_random);
        }

        _clock.Start();
        window.RequestAnimationFrame(Frame);
    }

    /// <summary>Records a completed startup milestone.</summary>
    /// <param name="completed">Number of completed startup steps.</param>
    internal void Progress(int completed)
    {
        _progress = completed;
        InvalidateVisual();
    }

    /// <summary>Allows the overlay to leave once the wordmark is whole.</summary>
    internal void Complete() => _ready = true;

    /// <summary>Jumps to the complete wordmark and lets the sky fade.</summary>
    internal void Skip()
    {
        if (CanSkip)
        {
            _time = _clock.Elapsed.TotalMilliseconds;
            _heldAt = _skipAt = _time;
            InvalidateVisual();
        }
    }

    /// <inheritdoc/>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Skip();
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (e.PreviousSize.Width > 0)
        {
            Skip();
        }
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _clock.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Advances the active sequence and requests the next frame.</summary>
    /// <param name="timestamp">The compositor timestamp.</param>
    private void Frame(TimeSpan timestamp)
    {
        if (!_clock.IsRunning || TopLevel.GetTopLevel(this) is not { } window)
        {
            return;
        }

        _time = _clock.Elapsed.TotalMilliseconds;
        if (double.IsNaN(_heldAt) && _time >= (_calm ? 300 : Hold))
        {
            _heldAt = _time;
        }

        if (double.IsNaN(_leavingAt) && !double.IsNaN(_heldAt) && (_ready || _time >= 6000))
        {
            _leavingAt = _time;
            IsHitTestVisible = false;
            Leaving?.Invoke(this, EventArgs.Empty);
        }

        if (DepartureTime >= (_calm ? 300 : 1600))
        {
            _clock.Stop();
            _sky = null;
            _shards = null;
            IsVisible = false;
            return;
        }

        InvalidateVisual();
        window.RequestAnimationFrame(Frame);
    }

    /// <summary>Paints the progress diamonds and delayed caption.</summary>
    /// <param name="context">The startup drawing context.</param>
    /// <param name="center">Horizontal center of the viewport.</param>
    /// <param name="wordBottom">Bottom edge of the wordmark.</param>
    /// <param name="exit">Milliseconds since the page started taking over.</param>
    private void DrawProgress(DrawingContext context, double center, double wordBottom, double exit)
    {
        if (double.IsNaN(_heldAt))
        {
            return;
        }

        var wait = _time - _heldAt;
        var move = double.IsNaN(exit) ? 1 : 1 - s_ease.Ease(Unit(exit / 300));
        var showing = s_ease.Ease(Unit((wait - 400) / 600)) * move;
        if (showing > 0)
        {
            using var alpha = context.PushOpacity(showing);
            for (var i = 0; i < 3; i++)
            {
                var x = center + ((i - 1) * 27);
                var y = wordBottom + 31.5;
                var lit = i < _progress;
                var pulse = !_calm && i == _progress ? 0.35 + (0.65 * (0.5 - (0.5 * Math.Cos(_time * Math.Tau / 1600)))) : 0.55;
                using var glow = context.PushOpacity(lit ? 1 : pulse);
                using var rotation = context.PushTransform(Matrix.CreateRotation(Math.PI / 4) * Matrix.CreateTranslation(x, y));
                var gold = new SolidColorBrush(s_gold);
                context.DrawRectangle(lit ? gold : null, new Pen(lit ? gold : s_dim), new Rect(-3, -3, 6, 6));
            }
        }

        var textFade = 0.85 * s_ease.Ease(Unit((wait - 2500) / 600)) * move;
        if (textFade > 0)
        {
            using var alpha = context.PushOpacity(textFade);
            var message = _progress == 2 ? _preparing : _connecting;
            var caption = new FormattedText(message, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 11.5, s_dim);
            context.DrawText(caption, new Point(center - (caption.Width / 2), wordBottom + 57));
        }
    }
}
