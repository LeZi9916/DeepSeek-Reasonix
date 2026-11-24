using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Diagnostics;
using static Reasonix.Studio.Shell.Startup.BootSky;

namespace Reasonix.Studio.Shell.Startup;

/// <summary>Presents the first-run welcome for an already configured workspace.</summary>
internal sealed partial class WelcomeCurtain : UserControl
{
    /// <summary>The short welcome leaves as the second line starts to appear.</summary>
    private const double Duration = 6200;

    /// <summary>The CSS visibility transition ends before the workspace takes over.</summary>
    private const double Fade = 260;

    /// <summary>The time the reference retains the curtain before mounting the workspace.</summary>
    private const double Handoff = 380;

    /// <summary>The easing used by the brand and greeting entrances.</summary>
    private static readonly SplineEasing s_out = new(0.16, 1, 0.3, 1);

    /// <summary>The easing used by the atmospheric drift and mark glow.</summary>
    private static readonly SplineEasing s_drift = new(0.42, 0, 0.58, 1);

    /// <summary>The reference default ease on the curtain's departure.</summary>
    private static readonly SplineEasing s_exit = new(0.25, 0.1, 0.25, 1);

    /// <summary>The timeline begins once the kernel says this machine owes a welcome.</summary>
    private readonly Stopwatch _clock = new();

    /// <summary>The wordmark light once it has entered.</summary>
    private readonly DropShadowEffect _light = new() { OffsetX = 0, OffsetY = 0 };

    /// <summary>The wordmark's entry blur.</summary>
    private readonly BlurEffect _markBlur = new();

    /// <summary>The time the user dismissed the curtain, or the automatic exit began.</summary>
    private double? _leaveAt;

    /// <summary>Initializes a new instance of the <see cref="WelcomeCurtain"/> class.</summary>
    public WelcomeCurtain() => InitializeComponent();

    /// <summary>Occurs after the curtain has given way to the conversation.</summary>
    internal event EventHandler? Finished;

    /// <summary>Gets the elapsed time of the last scene frame.</summary>
    internal double SceneTime
    {
        get; private set;
    }

    /// <summary>Gets the elapsed time of the departure, when it has begun.</summary>
    internal double? LeaveTime => SceneTime - _leaveAt;

    /// <summary>Plays the one-time welcome against the fixed viewport.</summary>
    /// <param name="language">The active interface language.</param>
    /// <param name="motion">Whether the system permits the animated introduction.</param>
    internal void Begin(InterfaceLanguage language, bool motion)
    {
        if (_clock.IsRunning || TopLevel.GetTopLevel(this) is not { } window)
        {
            return;
        }

        Label(language);
        _leaveAt = motion ? null : 0;
        SceneTime = 0;
        IsVisible = true;
        Focus();
        DrawScene();
        _clock.Restart();
        window.RequestAnimationFrame(Frame);
    }

    /// <summary>Refreshes the scene copy in the current interface language.</summary>
    /// <param name="language">The active interface language.</param>
    internal void Label(InterfaceLanguage language)
    {
        Greeting.Text = MessageCatalog.Get(MessageKey.Onboarding.WelcomeToReasonix, language);
        TaskTitle.Text = MessageCatalog.Get(MessageKey.Onboarding.YouHandItATask, language);
        TaskDetail.Text = MessageCatalog.Get(MessageKey.Onboarding.ReadingCodeCheckingFactsDoing, language);
        SkipHint.Text = MessageCatalog.Get(MessageKey.Onboarding.PressAnyKeyToSkip, language);
    }

    /// <inheritdoc/>
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        var size = e.NewSize;
        PlaceGlow(GoldGlow, size.Width * 0.46, size.Width * 0.04, size.Height * -0.12);
        PlaceGlow(BlueGlow, size.Width * 0.52, size.Width * 0.54, size.Height * 0.18);
        PlaceGlow(TealGlow, size.Width * 0.40, size.Width * 0.22, (size.Height * 1.18) - (size.Width * 0.40));
        Wordmark.Width = size.Width <= 640 ? 240 : 300;
        Wordmark.Height = Wordmark.Width * 392.25 / 1692.66;
        Studio.FontSize = size.Width <= 640 ? 17 : 22;
        Studio.LetterSpacing = Studio.FontSize * 0.21;
        Studio.LineHeight = Studio.FontSize;
        Greeting.FontSize = TaskTitle.FontSize = size.Width <= 640 ? 22 : 30;
        Greeting.LineHeight = TaskTitle.LineHeight = Greeting.FontSize * 1.35;
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        Skip();
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Skip();
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _clock.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Positions a still halo before its independent drift.</summary>
    /// <param name="glow">The outer cached halo.</param>
    /// <param name="diameter">The width and height of the halo.</param>
    /// <param name="x">Left edge in the viewport.</param>
    /// <param name="y">Top edge in the viewport.</param>
    private static void PlaceGlow(Border glow, double diameter, double x, double y)
    {
        glow.Width = glow.Height = diameter;
        Canvas.SetLeft(glow, x);
        Canvas.SetTop(glow, y);
    }

    /// <summary>Applies a translation and scale around the halo’s center.</summary>
    /// <param name="glow">The halo to move.</param>
    /// <param name="x">Horizontal travel as a fraction of the halo.</param>
    /// <param name="y">Vertical travel as a fraction of the halo.</param>
    /// <param name="scale">Scale around the center.</param>
    private static void Drift(Border glow, double x, double y, double scale)
    {
        var transform = (TransformGroup)glow.RenderTransform!;
        var zoom = (ScaleTransform)transform.Children[0];
        var move = (TranslateTransform)transform.Children[1];
        move.X = glow.Width * x;
        move.Y = glow.Height * y;
        zoom.ScaleX = zoom.ScaleY = scale;
    }

    /// <summary>Interpolates both halves of a CSS drift cycle.</summary>
    /// <param name="time">Milliseconds since the introduction began.</param>
    /// <param name="cycle">Milliseconds in one loop.</param>
    /// <returns>The keyframe interpolation amount.</returns>
    private static double Pulse(double time, double cycle) => s_drift.Ease(1 - Math.Abs((2 * (time % cycle) / cycle) - 1));

    /// <summary>Updates the movement shared by the lockup and text.</summary>
    /// <param name="target">The object on the curtain.</param>
    /// <param name="opacity">Its current opacity.</param>
    /// <param name="offset">Vertical displacement from the final location.</param>
    private static void Move(Control target, double opacity, double offset)
    {
        target.Opacity = opacity;
        ((TranslateTransform)target.RenderTransform!).Y = offset;
    }

    /// <summary>Converts CSS filter standard deviation to the radius consumed by the Skia effect.</summary>
    /// <param name="sigma">The reference CSS filter length in device-independent pixels.</param>
    /// <returns>The Avalonia effect radius.</returns>
    private static double EffectRadius(double sigma) => Math.Max(0, (sigma - 0.5) / 0.288675);

    /// <summary>Applies the three parts of the original greeting entrance and exit.</summary>
    /// <param name="target">The sentence or sentence and detail.</param>
    /// <param name="phase">The position within its animation, from zero to one.</param>
    /// <param name="baseline">Baseline adjustment for the native line box.</param>
    private static void Say(Control target, double phase, double baseline = 0)
    {
        var entry = s_out.Ease(Unit(phase / 0.18));
        var exit = s_out.Ease(Unit((phase - 0.72) / 0.28));
        Move(target, entry * (1 - exit), baseline + Mix(12, 0, entry) - (10 * exit));
        ((BlurEffect)target.Effect!).Radius = EffectRadius(Mix(4, 0, entry) + (3 * exit));
    }

    /// <summary>Enters the same departure path as the automatic timer.</summary>
    private void Skip()
    {
        if (_clock.IsRunning)
        {
            _leaveAt ??= _clock.Elapsed.TotalMilliseconds;
        }
    }

    /// <summary>Advances the welcome and dismisses it exactly once.</summary>
    /// <param name="timestamp">The compositor time of this callback.</param>
    private void Frame(TimeSpan timestamp)
    {
        if (!_clock.IsRunning || TopLevel.GetTopLevel(this) is not { } window)
        {
            return;
        }

        SceneTime = _clock.Elapsed.TotalMilliseconds;
        if (SceneTime >= Duration)
        {
            _leaveAt ??= Duration;
        }

        if (LeaveTime >= Handoff)
        {
            _clock.Stop();
            IsVisible = false;
            Finished?.Invoke(this, EventArgs.Empty);
            return;
        }

        DrawScene();
        window.RequestAnimationFrame(Frame);
    }

    /// <summary>Evaluates the reference keyframes against a single clock.</summary>
    private void DrawScene()
    {
        var leaving = LeaveTime;
        var time = SceneTime;
        Opacity = leaving is { } elapsed ? 1 - s_exit.Ease(Unit(elapsed / Fade)) : 1;
        var t1 = Pulse(leaving is null ? time : 0, 24000);
        var t2 = Pulse(leaving is null ? time : 0, 30000);
        var t3 = Pulse(leaving is null ? time : 0, 27000);
        Drift(GoldGlow, 0.14 * t1, 0.16 * t1, Mix(1, 1.22, t1));
        Drift(BlueGlow, -0.16 * t2, -0.12 * t2, leaving is null ? Mix(1.1, 0.92, t2) : 1);
        Drift(TealGlow, 0.10 * t3, -0.18 * t3, leaving is null ? Mix(0.95, 1.18, t3) : 1);

        var mark = leaving is null ? s_out.Ease(Unit((time - 260) / 900)) : 1;
        var studio = leaving is null ? s_out.Ease(Unit((time - 1500) / 900)) : 1;
        Move(Wordmark, mark, 10 * (1 - mark));
        Move(Studio, studio, (10 * (1 - studio)) - 4);
        ((BlurEffect)Studio.Effect!).Radius = EffectRadius(6 * (1 - studio));

        // The reference subtitle keeps its underlying zero opacity until the play state is removed.
        Subtitle.Opacity = leaving is null ? 0 : 1;
        Say(Greeting, leaving is null ? Unit((time - 4000) / 2600) : 1, baseline: -2);
        Say(TaskLine, leaving is null ? Unit((time - 6000) / 2600) : 1);
        Progress.Width = leaving is null ? 148 * Unit(time / 9400) : 0;
        SetWordmarkLight(mark, leaving is null ? time : 0);
    }

    /// <summary>Transitions the warm flash back to the blue wordmark shadow.</summary>
    /// <param name="entry">The current wordmark entrance fraction.</param>
    /// <param name="time">Milliseconds since the intro began.</param>
    private void SetWordmarkLight(double entry, double time)
    {
        if (entry < 1)
        {
            _markBlur.Radius = EffectRadius(6 * (1 - entry));
            Wordmark.Effect = _markBlur;
            Wordmark.Paint(Color.FromRgb(1, 83, 229));
            return;
        }

        var glow = Unit((time - 1250) / 1600);
        var warming = time >= 1250 && glow < 0.32;
        var phase = s_drift.Ease(warming ? glow / 0.32 : Unit((glow - 0.32) / 0.68));
        double brightness;
        if (time < 1250)
        {
            _light.Color = Color.FromRgb(1, 83, 229);
            _light.BlurRadius = EffectRadius(18);
            _light.Opacity = 0.45;
            brightness = 1;
        }
        else if (warming)
        {
            _light.Color = Color.FromRgb(255, (byte)Mix(215, 233, phase), (byte)Mix(130, 184, phase));
            _light.BlurRadius = EffectRadius(Mix(5, 26, phase));
            _light.Opacity = Mix(0.9, 0.85, phase);
            brightness = Mix(1.45, 1.25, phase);
        }
        else
        {
            _light.Color = Color.FromRgb((byte)Mix(255, 1, phase), (byte)Mix(233, 83, phase), (byte)Mix(184, 229, phase));
            _light.BlurRadius = EffectRadius(Mix(26, 18, phase));
            _light.Opacity = Mix(0.85, 0.45, phase);
            brightness = Mix(1.25, 1, phase);
        }

        Wordmark.Effect = _light;
        Wordmark.Paint(Color.FromRgb((byte)brightness, (byte)Math.Min(255, Math.Round(83 * brightness)), (byte)Math.Min(255, Math.Round(229 * brightness))));
    }
}
