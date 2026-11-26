using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Diagnostics;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>A titled settings group within a card.</summary>
internal sealed partial class SettingsGroup : UserControl
{
    /// <summary>The reference ease-out curve for the landing outline.</summary>
    private static readonly SplineEasing s_landingEase = new(0.16, 1, 0.3, 1);

    /// <summary>Identifies the current landing fade.</summary>
    private int _landingMotion;

    /// <summary>Initializes a new instance of the <see cref="SettingsGroup"/> class.</summary>
    /// <param name="key">The title and navigation key.</param><param name="hint">The descriptive message key.</param>
    /// <param name="body">The group contents.</param><param name="language">The window language.</param>
    /// <param name="scope">The scope key when different from the page default.</param>
    internal SettingsGroup(string key, string? hint, Control body, InterfaceLanguage language, string? scope = null)
    {
        InitializeComponent();
        Tag = key;
        Heading.Text = MessageCatalog.Get(key, language);
        Hint.IsVisible = hint is not null;
        Hint.Text = hint is null ? null : MessageCatalog.Get(hint, language);
        if (hint is null)
        {
            Items.Margin = default;
        }

        ScopeBadge.IsVisible = scope is not null;
        ScopeLabel.Text = scope is null ? null : MessageCatalog.Get(scope, language);
        Items.Content = body;
    }

    /// <summary>Flashes the reference 3px outline, offset 2px, at 66% accent when navigation brings this group into view.</summary>
    internal void FlashLanding()
    {
        ClearLanding();
        if (TopLevel.GetTopLevel(this) is not Window window || !window.Classes.Contains("motion"))
        {
            return;
        }

        LandingRing.Opacity = 0.66;
        LandingRing.IsVisible = true;
        PlayLanding(window, ++_landingMotion);
    }

    /// <summary>Removes a pending landing without waiting for its animation.</summary>
    internal void ClearLanding()
    {
        _landingMotion++;
        LandingRing.IsVisible = false;
        LandingRing.Opacity = 0;
    }

    /// <summary>Fades the fixed landing ink so it reads as an outline color returning to transparent.</summary>
    /// <param name="window">The window providing render frames.</param>
    /// <param name="generation">The current landing.</param>
    private void PlayLanding(Window window, int generation)
    {
        Stopwatch? clock = null;
        window.RequestAnimationFrame(Frame);

        void Frame(TimeSpan timestamp)
        {
            if (generation != _landingMotion)
            {
                return;
            }

            clock ??= Stopwatch.StartNew();
            var progress = Math.Clamp(clock.Elapsed.TotalMilliseconds / 900, 0, 1);
            if (progress >= 1 || !window.Classes.Contains("motion") || TopLevel.GetTopLevel(this) != window)
            {
                ClearLanding();
                return;
            }

            LandingRing.Opacity = 0.66 * (1 - s_landingEase.Ease(progress));
            window.RequestAnimationFrame(Frame);
        }
    }
}
