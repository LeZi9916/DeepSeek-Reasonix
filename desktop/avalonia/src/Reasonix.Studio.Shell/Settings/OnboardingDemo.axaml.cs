using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Diagnostics;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Replays the welcome screen's seven-second example conversation.</summary>
internal sealed partial class OnboardingDemo : UserControl
{
    /// <summary>The time between successive examples.</summary>
    private const int CycleMs = 7000;

    /// <summary>The row entrance curve used by the reference.</summary>
    private static readonly SplineEasing s_enter = new(0.16, 1, 0.3, 1);

    /// <summary>The curve of the stroke reveal.</summary>
    private static readonly SplineEasing s_draw = new(0.42, 0, 0.58, 1);

    /// <summary>The staggered rows and their motion transforms.</summary>
    private readonly (Control Row, TranslateTransform Transform, int Delay)[] _rows;

    /// <summary>The clock shared by every row and the final fade.</summary>
    private readonly Stopwatch _clock = new();

    /// <summary>Paints each new frame while playback is active.</summary>
    private readonly DispatcherTimer _frames = new() { Interval = TimeSpan.FromMilliseconds(1000d / 60) };

    /// <summary>Elapsed clock time at which to replace the current round.</summary>
    private double _nextRound;

    /// <summary>Whether the welcome screen currently owns the example.</summary>
    private bool _visiting;

    /// <summary>Whether animation is permitted.</summary>
    private bool _motion;

    /// <summary>Whether the reader requested the final frame.</summary>
    private bool _finished;

    /// <summary>Initializes a new instance of the <see cref="OnboardingDemo"/> class.</summary>
    public OnboardingDemo()
    {
        InitializeComponent();
        _rows =
        [
            (RequestRow, new TranslateTransform(), 300),
            (ThinkingRow, new TranslateTransform(), 1200),
            (ReadRow, new TranslateTransform(), 3000),
            (EditRow, new TranslateTransform(), 3900),
            (RunRow, new TranslateTransform(), 4800),
            (ResultRow, new TranslateTransform(), 5700),
        ];
        foreach (var (row, transform, _) in _rows)
        {
            row.RenderTransform = transform;
        }

        _frames.Tick += (_, _) => DrawFrame();
        PointerEntered += (_, _) => Pause();
        PointerExited += (_, _) => Resume();
        PointerPressed += (_, args) =>
        {
            if (args.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _finished = true;
                Refresh();
                args.Handled = true;
            }
        };
    }

    /// <summary>Localizes the text from the reference example.</summary>
    /// <param name="language">The shell's language.</param>
    internal void Label(InterfaceLanguage language)
    {
        RequestLabel.Text = MessageCatalog.Get(MessageKey.Onboarding.DemoAsk, language);
        ThinkingLabel.Text = MessageCatalog.Get(MessageKey.Onboarding.DemoThinking, language);
        ReadLabel.Text = MessageCatalog.Get(MessageKey.Onboarding.DemoRead, language);
        EditLabel.Text = MessageCatalog.Get(MessageKey.Onboarding.DemoEdit, language);
        RunLabel.Text = MessageCatalog.Get(MessageKey.Onboarding.DemoRun, language);
        ResultLabel.Text = MessageCatalog.Get(MessageKey.Onboarding.DemoDone, language);
    }

    /// <summary>Starts a new visit to the welcome screen.</summary>
    internal void Begin()
    {
        _finished = false;
        _visiting = true;
        _clock.Reset();
        Refresh();
    }

    /// <summary>Stops playback when leaving the welcome stage.</summary>
    internal void End()
    {
        _visiting = false;
        Pause();
    }

    /// <summary>Applies the current motion preference to the demo.</summary>
    /// <param name="allowed">Whether the system and shell allow motion.</param>
    internal void SetMotion(bool allowed)
    {
        if (_motion != allowed)
        {
            _motion = allowed;
            Refresh();
        }
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && _visiting)
        {
            Refresh();
        }
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        End();
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Draws a final frame or schedules the current round.</summary>
    private void Refresh()
    {
        if (!_visiting || !IsVisible || !_motion || _finished)
        {
            Pause();
            Sequence.Opacity = 1;
            ThinkingRow.IsVisible = false;
            foreach (var (row, transform, _) in _rows)
            {
                row.Opacity = 1;
                transform.Y = 0;
            }

            return;
        }

        ThinkingRow.IsVisible = true;
        DrawFrame();
        Resume();
    }

    /// <summary>Holds the clock and stops requesting frames.</summary>
    private void Pause()
    {
        _clock.Stop();
        _frames.Stop();
    }

    /// <summary>Continues the paused frame and rearms the next round.</summary>
    private void Resume()
    {
        if (_visiting && IsVisible && _motion && !_finished && !IsPointerOver)
        {
            _nextRound = _clock.Elapsed.TotalMilliseconds + CycleMs;
            _clock.Start();
            _frames.Start();
        }
    }

    /// <summary>Evaluates the stagger, thinking collapse and final fade on one timeline.</summary>
    private void DrawFrame()
    {
        var elapsed = _clock.Elapsed.TotalMilliseconds;
        if (_clock.IsRunning && elapsed >= _nextRound)
        {
            _clock.Restart();
            _nextRound = CycleMs;
            elapsed = 0;
        }

        foreach (var (row, transform, delay) in _rows)
        {
            var progress = s_enter.Ease(Math.Clamp((elapsed - delay) / 420, 0, 1));
            row.Opacity = progress;
            transform.Y = 6 * (1 - progress);
        }

        var folding = 1 - Math.Clamp((elapsed - 3200) / 300, 0, 1);
        ThinkingRow.Opacity *= folding;
        ThinkingRow.Height = 16.8 * folding;
        var strokeElapsed = elapsed % 1200;
        var drawing = 1 - s_draw.Ease(Math.Clamp(strokeElapsed / 720, 0, 1));
        Stem.StrokeDashOffset = 6 * drawing;
        Bowl.StrokeDashOffset = 10.3 * drawing;
        Leg.StrokeDashOffset = 3.24 * drawing;
        Sequence.Opacity = 1 - Math.Clamp((elapsed - 6510) / 490, 0, 1);
    }
}
