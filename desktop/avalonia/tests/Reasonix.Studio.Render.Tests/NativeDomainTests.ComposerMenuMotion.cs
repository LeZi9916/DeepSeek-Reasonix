using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Shell.Composer;
using Reasonix.Studio.Shell.Host;
using System;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks the reference menu poses, the retained exit and focus restoration.</summary>
    /// <param name="dark">Whether to capture the dark palette.</param>
    /// <param name="kind">The composer menu to open.</param>
    /// <returns>The popup transition check.</returns>
    [Theory]
    [InlineData(false, "mode")]
    [InlineData(true, "mode")]
    [InlineData(false, "policy")]
    [InlineData(true, "policy")]
    [InlineData(false, "model")]
    [InlineData(true, "model")]
    public Task ComposerMenuMotionMatchesReference(bool dark, string kind) => WithSettingsAsync(
        "conversation",
        async (window, reads) =>
        {
            window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            await WaitForMotionFrame(() => window.Classes.Contains("motion")).ConfigureAwait(true);
            window.AdoptComposer(reads.Read<SessionStatus>("/status"));
            var (triggerName, menuName, stem, clip) = kind switch
            {
                "policy" => ("ApprovalMode", "PolicyPopup", "compose-policy-popup", new Rect(724, 429, 382, 416)),
                "model" => ("ModelButton", "ModelPicker", "compose-model-popup", new Rect(571, 364, 642, 480)),
                _ => ("PlanMode", "ModePopup", "compose-popup", new Rect(557, 649, 342, 196)),
            };
            var trigger = window.FindControl<Button>(triggerName)!;
            var menu = window.FindControl<UserControl>(menuName)!;
            var surface = menu.FindControl<Popup>("Surface")!;
            var frame = menu.FindControl<Border>("Frame")!;

            trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var threshold = kind == "policy" ? 0.87 : 0.75;
            await WaitForMotionFrame(() => frame.Opacity >= threshold && frame.Bounds.Height > 0).ConfigureAwait(true);
            Assert.True(surface.IsOpen);
            Assert.InRange(frame.Opacity, threshold, 0.9999);
            Assert.InRange(frame.RenderTransform!.Value.M22, 0.985, 1.0);
            Assert.True(frame.RenderTransform!.Value.M32 < 0);
            Capture("entry");
            await WaitForMotionFrame(() => frame.Opacity == 1).ConfigureAwait(true);
            Assert.Equal(0, frame.RenderTransform!.Value.M32);
            var settled = frame.TranslatePoint(default, window)!.Value;
            Assert.InRange(settled.X - clip.X, 11.5, 14.5);
            Assert.InRange(settled.Y - clip.Y, 11.5, 14.5);
            Assert.Equal(clip.Width - 42, frame.Bounds.Width, 0);
            Assert.Equal(clip.Height - 40, frame.Bounds.Height, 0);
            Capture("settled");

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.False(frame.IsHitTestVisible);
            Assert.True(trigger.IsFocused);
            Assert.DoesNotContain("open", trigger.Classes);
            var exitThreshold = kind == "policy" ? 0.035 : 0.095;
            await WaitForMotionFrame(() => frame.Opacity > 0.002 && frame.Opacity <= exitThreshold).ConfigureAwait(true);
            Assert.True(surface.IsOpen);
            Assert.True(frame.RenderTransform!.Value.M32 < -3);
            Capture("exit");
            await WaitForMotionFrame(() => !surface.IsOpen).ConfigureAwait(true);

            void Capture(string phase)
            {
                var parent = (Control)frame.GetVisualParent()!;
                var at = parent.TranslatePoint(frame.Bounds.TopLeft, window)!.Value;
                var scheme = dark ? "dark" : "light";
                var dx = (int)clip.X - (int)Math.Floor(at.X);
                var top = (int)Math.Floor(at.Y) - (int)clip.Y;
                CaptureMotionRegion(window, frame, dx, top, (int)clip.Width, (int)clip.Height, $"{stem}-{scheme}-{phase}");
            }
        },
        workspacePreferences: new WorkspacePreferences { ShowModePicker = true },
        allowMotion: true);

    /// <summary>Exercises reversals and an outside click that reaches the draft through the fading overlay.</summary>
    /// <returns>The mounted overlay input check.</returns>
    [Fact]
    public Task ComposerMenuMotionReversesAndPassesOutsideInput() => WithSettingsAsync(
        "conversation",
        async (window, _) =>
        {
            await WaitForMotionFrame(() => window.Classes.Contains("motion")).ConfigureAwait(true);
            var trigger = window.FindControl<Button>("PlanMode")!;
            var popup = window.FindControl<ComposerPopup>("ModePopup")!;
            var surface = popup.FindControl<Popup>("Surface")!;
            var frame = popup.FindControl<Border>("Frame")!;
            trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForMotionFrame(() => frame.Opacity == 1).ConfigureAwait(true);

            popup.Close();
            await WaitForMotionFrame(() => frame.Opacity is > 0.3 and < 0.8).ConfigureAwait(true);
            var at = frame.Opacity;
            trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(popup.IsOpen);
            Assert.True(frame.IsHitTestVisible);
            Assert.InRange(frame.Opacity, at, 1);
            await WaitForMotionFrame(() => frame.Opacity == 1).ConfigureAwait(true);

            var composer = window.FindControl<TextBox>("Composer")!;
            var point = composer.TranslatePoint(new Point(composer.Bounds.Width - 25, 15), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.False(popup.IsOpen);
            Assert.True(composer.IsFocused);
            await WaitForMotionFrame(() => !surface.IsOpen).ConfigureAwait(true);
        },
        workspacePreferences: new WorkspacePreferences { ShowModePicker = true },
        allowMotion: true);

    /// <summary>Applies open and close synchronously when desktop motion is disabled.</summary>
    /// <returns>The reduced-motion transition check.</returns>
    [Fact]
    public Task ComposerMenuMotionHonorsReducedMotion() => WithSettingsAsync("conversation", (window, _) =>
    {
        var trigger = window.FindControl<Button>("ApprovalMode")!;
        var popup = window.FindControl<ComposerPopup>("PolicyPopup")!;
        var surface = popup.FindControl<Popup>("Surface")!;
        var frame = popup.FindControl<Border>("Frame")!;
        trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(surface.IsOpen);
        Assert.Equal(1, frame.Opacity);
        popup.Close();
        Assert.False(surface.IsOpen);
        return Task.CompletedTask;
    });
}
