using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Shell.Workbench;
using Reasonix.Studio.State;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Samples the backgrounds painted by the mounted controls on pointer entry and exit.</summary>
    /// <param name="dark">Whether to use the dark palette.</param>
    /// <param name="workbench">Whether to open the workbench fixture.</param>
    /// <returns>The hover and return-to-rest check.</returns>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public Task ButtonHoverFramesFollowPointerEntryAndExit(bool dark, bool workbench) => WithSettingsAsync(
        workbench ? "workbench" : "conversation",
        async (window, reads) =>
        {
            await WaitForSettingsFrame(() => window.Classes.Contains("motion")).ConfigureAwait(true);
            window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            window.UpdateLayout();

            if (workbench)
            {
                ClickAt(window, "action:workbench.new");
                ClickAt(window, "action:workbench.launch-browser");
                window.UpdateLayout();
                var tab = window.FindControl<StackPanel>("FileTabs")!.Children
                    .OfType<WorkbenchHeaderTab>().Single(control => Equals(control.Tag, "README.md"));
                Assert.DoesNotContain("active", tab.Classes);
                await WaitForMotionFrame(() => !tab.IsAnimating(Visual.OpacityProperty)).ConfigureAwait(true);
                await Sample(tab, tab.FindControl<Border>("Frame")!, Border.BackgroundProperty, "file-tab").ConfigureAwait(true);
                var close = ButtonNamed("CloseWorkbench");
                await Sample(close, close, Button.BackgroundProperty, "dismiss").ConfigureAwait(true);
                return;
            }

            window.AdoptComposer(reads.Read<SessionStatus>("/status"));
            window.UpdateLayout();
            var chrome = ButtonNamed("ViewSwitch");
            var presenter = chrome.GetVisualDescendants().OfType<ContentPresenter>().Single(control => control.Name == "PART_ContentPresenter");
            await Sample(chrome, presenter, ContentPresenter.BackgroundProperty, "chrome").ConfigureAwait(true);
            var settings = ButtonNamed("OpenSettings");
            var settingsSurface = settings.GetVisualDescendants().OfType<Border>().Single(control => control.Name == "SettingsButtonSurface");
            await Sample(settings, settingsSurface, Border.BackgroundProperty, "settings").ConfigureAwait(true);
            foreach (var (name, stem) in new[] { ("Attach", "attach"), ("SideQuestion", "side"), ("RefinePrompt", "refine"), ("PlanMode", "mode"), ("ContextToggle", "context") })
            {
                var button = ButtonNamed(name);
                var icon = button.Classes.Contains("compose-icon")
                    ? button.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single()
                    : null;
                await Sample(button, button, Button.BackgroundProperty, stem, icon).ConfigureAwait(true);
            }

            window.FindControl<TextBox>("Composer")!.Text = "Hover";
            var send = ButtonNamed("Send");
            await WaitForMotionFrame(() => send.IsEnabled && send.IsEffectivelyVisible).ConfigureAwait(true);
            await Sample(send, send, Button.BackgroundProperty, "send").ConfigureAwait(true);

            window.ShowState(new SessionState { Running = true });
            window.UpdateLayout();
            var stop = ButtonNamed("Stop");
            var stopped = await Sample(stop, stop, Button.BackgroundProperty, "stop").ConfigureAwait(true);
            Assert.Equal(0.12, stopped.Baseline, 2);
            Assert.InRange(stopped.Enter, 0.121, 0.199);
            Assert.Equal(0.20, stopped.Hover, 2);
            Assert.InRange(stopped.Exit, 0.121, 0.199);

            Button ButtonNamed(string name) => window.FindControl<Button>(name)!;

            async Task<(double Baseline, double Enter, double Hover, double Exit)> Sample(
                Control anchor, Control surface, AvaloniaProperty<IBrush?> property, string stem, Avalonia.Controls.Shapes.Path? icon = null)
            {
                var outside = new Point(700, 80);
                window.MouseMove(outside);
                await WaitForMotionFrame(() => !surface.IsAnimating(property)).ConfigureAwait(true);
                window.UpdateLayout();
                Assert.True(anchor.IsEffectivelyVisible);
                var inside = anchor.TranslatePoint(new Point(anchor.Bounds.Width / 2, anchor.Bounds.Height / 2), window)!.Value;
                var rest = Opacity();
                var restingInk = Ink();
                Capture("rest");
                window.MouseMove(inside);
                Assert.True(anchor.IsPointerOver, stem);
                await Task.Delay(35).ConfigureAwait(true);
                var enter = Opacity();
                Capture("enter");
                await WaitForMotionFrame(() => !surface.IsAnimating(property)).ConfigureAwait(true);
                var hover = Opacity();
                Capture("hover");
                if (icon is not null)
                {
                    Assert.True(window.TryFindResource("TextBrush", window.ActualThemeVariant, out var resource));
                    Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(resource).Color, Ink());
                    Assert.NotEqual(restingInk, Ink());
                }

                window.MouseMove(outside);
                Assert.False(anchor.IsPointerOver);
                await Task.Delay(35).ConfigureAwait(true);
                var exit = Opacity();
                Capture("exit");
                await WaitForMotionFrame(() => !surface.IsAnimating(property)).ConfigureAwait(true);
                Assert.Equal(rest, Opacity(), 4);
                Assert.Equal(restingInk, Ink());
                Capture("restored");
                return (rest, enter, hover, exit);

                Color? Ink() => (icon?.Stroke as ISolidColorBrush)?.Color;

                double Opacity() => surface.GetValue(property) is ISolidColorBrush brush ? brush.Opacity * brush.Color.A / 255 : 0;

                void Capture(string phase) => CaptureMotionRegion(
                    window,
                    anchor,
                    -4,
                    4,
                    (int)Math.Ceiling(anchor.Bounds.Width) + 8,
                    (int)Math.Ceiling(anchor.Bounds.Height) + 8,
                    $"button-{stem}-{(dark ? "dark" : "light")}-{phase}");
            }
        },
        workspacePreferences: new()
        {
            ShowModePicker = true,
        },
        allowMotion: true);
}
