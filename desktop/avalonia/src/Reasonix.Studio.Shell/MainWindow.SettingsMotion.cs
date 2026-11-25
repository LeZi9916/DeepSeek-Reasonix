using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Reasonix.Studio.Shell.Settings;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The reference ease-out curve shared by settings motion.</summary>
    private static readonly SplineEasing s_settingsEase = new(0.16, 1, 0.3, 1);

    /// <summary>The reference scroll curve measured from the browser's smooth scrolling.</summary>
    private static readonly SplineEasing s_settingsScrollEase = new(0.25, 0.1, 0.25, 1);

    /// <summary>Identifies the current section entrance.</summary>
    private int _settingsColumnMotion;

    /// <summary>Identifies the current page-index scroll.</summary>
    private int _settingsScrollMotion;

    /// <summary>The start and end offsets of the current page-index scroll.</summary>
    private (Vector From, Vector To) _settingsScroll;

    /// <summary>The group carrying the current landing outline.</summary>
    private SettingsGroup? _settingsLanded;

    /// <summary>Replays the reference section entrance after the column is replaced.</summary>
    private void EnterSettingsColumn()
    {
        ClearSettingsLanding();
        CancelSettingsScroll();
        StopSettingsColumn();
        var content = SettingsContent;
        if (content.RenderTransform is not TranslateTransform move)
        {
            move = new TranslateTransform();
            content.RenderTransform = move;
        }

        content.Opacity = 1;
        move.Y = 0;
        if (!Classes.Contains("motion") || _closing.IsCancellationRequested)
        {
            return;
        }

        content.Opacity = 0;
        move.Y = 6;
        _ = PlaySettingsColumnAsync(++_settingsColumnMotion, content, move);
    }

    /// <summary>Brings a setting below the pinned index, then plays the group landing when it has one.</summary>
    /// <param name="target">The mounted setting or group identified by its anchor tag.</param>
    private void LandOnSetting(Control target)
    {
        ClearSettingsLanding();
        if (target is SettingsGroup group)
        {
            _settingsLanded = group;
            group.FlashLanding();
        }

        SettingsScroller.UpdateLayout();
        if (target.TranslatePoint(default, SettingsScroller) is not { } at)
        {
            return;
        }

        var clearance = (SettingsIndex.IsVisible ? SettingsIndex.Bounds.Height : 0) + 16;
        var from = SettingsScroller.Offset;
        var maximum = Math.Max(0, SettingsScroller.ScrollBarMaximum.Y);
        var destination = new Vector(from.X, Math.Clamp(from.Y + at.Y - clearance, 0, maximum));
        ScrollSettings(from, destination);
    }

    /// <summary>Stops a pending page-index scroll at its current offset.</summary>
    private void CancelSettingsScroll() => _settingsScrollMotion++;

    /// <summary>Removes the previous landing outline when the reader opens another section.</summary>
    private void ClearSettingsLanding()
    {
        _settingsLanded?.ClearLanding();
        _settingsLanded = null;
    }

    /// <summary>Stops the section entrance and optionally settles it at its final pose.</summary>
    private void StopSettingsColumn()
    {
        _settingsColumnMotion++;
        if (SettingsContent.RenderTransform is not TranslateTransform move)
        {
            return;
        }

        SettingsContent.Opacity = 1;
        move.Y = 0;
    }

    /// <summary>Fades and lifts the replaced section content into place over 120 ms.</summary>
    /// <param name="generation">The entrance this loop owns.</param>
    /// <param name="content">The settings column wrapper.</param>
    /// <param name="move">Its translation transform.</param>
    /// <returns>The completed entrance.</returns>
    private async Task PlaySettingsColumnAsync(int generation, Control content, TranslateTransform move)
    {
        var clock = Stopwatch.StartNew();
        while (generation == _settingsColumnMotion)
        {
            var progress = Math.Clamp(clock.Elapsed.TotalMilliseconds / 120, 0, 1);
            var eased = s_settingsEase.Ease(progress);
            content.Opacity = eased;
            move.Y = 6 * (1 - eased);
            if (progress >= 1)
            {
                content.Opacity = 1;
                move.Y = 0;
                return;
            }

            await Task.Delay(16).ConfigureAwait(true);
        }
    }

    /// <summary>Glides the settings column to a destination over the reference smooth-scroll pace.</summary>
    /// <param name="from">The current offset.</param>
    /// <param name="destination">The measured target offset.</param>
    private void ScrollSettings(Vector from, Vector destination)
    {
        CancelSettingsScroll();
        var distance = Math.Abs(destination.Y - from.Y);
        if (!Classes.Contains("motion") || _closing.IsCancellationRequested || distance < 1)
        {
            SettingsScroller.Offset = destination;
            return;
        }

        _settingsScroll = (from, destination);
        _ = PlaySettingsScrollAsync(_settingsScrollMotion);
    }

    /// <summary>Advances a page-index scroll over 300 ms.</summary>
    /// <param name="generation">The scroll this loop owns.</param>
    /// <returns>The completed scroll.</returns>
    private async Task PlaySettingsScrollAsync(int generation)
    {
        var clock = Stopwatch.StartNew();

        // Reference smooth scrolling measured ~300 ms over 313 px and ~750 ms over 1986 px.
        var distance = Math.Abs(_settingsScroll.To.Y - _settingsScroll.From.Y);
        var duration = Math.Clamp(300 + (Math.Max(0, distance - 400) * 0.275), 300, 800);
        while (generation == _settingsScrollMotion)
        {
            var progress = Math.Clamp(clock.Elapsed.TotalMilliseconds / duration, 0, 1);
            var eased = s_settingsScrollEase.Ease(progress);
            SettingsScroller.Offset = new Vector(
                _settingsScroll.From.X + ((_settingsScroll.To.X - _settingsScroll.From.X) * eased),
                _settingsScroll.From.Y + ((_settingsScroll.To.Y - _settingsScroll.From.Y) * eased));
            if (progress >= 1)
            {
                SettingsScroller.Offset = _settingsScroll.To;
                return;
            }

            await Task.Delay(16).ConfigureAwait(true);
        }
    }
}
