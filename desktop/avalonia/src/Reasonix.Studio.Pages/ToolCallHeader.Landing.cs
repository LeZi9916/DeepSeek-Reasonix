using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using System;
using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Pages;

internal sealed partial class ToolCallHeader
{
    /// <summary>The remaining heat of the tool title's landing highlight.</summary>
    private static readonly StyledProperty<double> s_landingHeat = AvaloniaProperty.Register<ToolCallHeader, double>("LandingHeat");

    /// <summary>Cancels a glow superseded by a new landing or by teardown.</summary>
    private CancellationTokenSource? _landingGlow;

    /// <summary>Highlights the tool name and arguments after an explicit jump to this call.</summary>
    /// <returns>The visible landing highlight.</returns>
    internal async Task FlashLandingAsync()
    {
        if (_landingGlow is not null || TopLevel.GetTopLevel(this) is not Window window || !window.Classes.Contains("motion")
            || !this.TryFindResource("Accent", ActualThemeVariant, out var accent) || accent is not Color shadowColor
            || !this.TryFindResource("AccentInk", ActualThemeVariant, out var ink) || ink is not Color inkColor)
        {
            return;
        }

        using var lifetime = new CancellationTokenSource();
        _landingGlow = lifetime;
        var title = this.FindControl<TextBlock>("ToolName")!;
        var argument = this.FindControl<TextBlock>("Argument")!;
        var titleColor = (title.GetBaseValue(TextBlock.ForegroundProperty).GetValueOrDefault() as ISolidColorBrush)?.Color ?? inkColor;
        var argColor = (argument.GetBaseValue(TextBlock.ForegroundProperty).GetValueOrDefault() as ISolidColorBrush)?.Color ?? inkColor;
        var titleBrush = new SolidColorBrush(inkColor);
        var argBrush = new SolidColorBrush(inkColor);
        var glow = new DropShadowEffect { Color = shadowColor, OffsetX = 0, OffsetY = 0, BlurRadius = 7, Opacity = 0.44 };
        using var titleInk = title.SetValue(TextBlock.ForegroundProperty, titleBrush, BindingPriority.Animation);
        using var argInk = argument.SetValue(TextBlock.ForegroundProperty, argBrush, BindingPriority.Animation);
        using var titleEffect = title.SetValue(EffectProperty, glow, BindingPriority.Animation);
        using var argEffect = argument.SetValue(EffectProperty, glow, BindingPriority.Animation);
        PropertyChanged += DrawGlow;
        DetachedFromVisualTree += Cancel;
        window.Classes.CollectionChanged += MotionChanged;
        try
        {
            var animation = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(1100),
                Easing = new SplineEasing(0.16, 1, 0.3, 1),
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(s_landingHeat, 1d) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(s_landingHeat, 0d) } },
                },
            };
            await animation.RunAsync(this, lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            PropertyChanged -= DrawGlow;
            DetachedFromVisualTree -= Cancel;
            window.Classes.CollectionChanged -= MotionChanged;
            if (ReferenceEquals(_landingGlow, lifetime))
            {
                _landingGlow = null;
            }
        }

        void DrawGlow(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == s_landingHeat)
            {
                var heat = GetValue(s_landingHeat);
                titleBrush.Color = Mix(titleColor, inkColor, heat);
                argBrush.Color = Mix(argColor, inkColor, heat);
                glow.BlurRadius = 7 * heat;
                glow.Opacity = 0.44 * heat;
            }
        }

        void Cancel(object? sender, VisualTreeAttachmentEventArgs args) => lifetime.Cancel();

        void MotionChanged(object? sender, NotifyCollectionChangedEventArgs args)
        {
            if (!window.Classes.Contains("motion"))
            {
                lifetime.Cancel();
            }
        }
    }

    /// <summary>Interpolates the normal foreground and the reference accent ink.</summary>
    /// <param name="normal">The final foreground of this text run.</param>
    /// <param name="ink">The initial highlight colour.</param>
    /// <param name="heat">The visible fraction of the highlight.</param>
    /// <returns>The current foreground colour.</returns>
    private static Color Mix(Color normal, Color ink, double heat) =>
        Color.FromRgb(
            (byte)Math.Round(normal.R + ((ink.R - normal.R) * heat)),
            (byte)Math.Round(normal.G + ((ink.G - normal.G) * heat)),
            (byte)Math.Round(normal.B + ((ink.B - normal.B) * heat)));
}
