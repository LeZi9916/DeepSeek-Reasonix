using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Kernel.Events;
using Reasonix.Studio.Kernel.Session;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Session;
using Reasonix.Studio.Shell.Workbench;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Checks the painted tab frames and the hue of intermediate pointer states.</summary>
    /// <param name="dark">Whether to use the dark palette.</param>
    /// <param name="scene">The page that owns the tab.</param>
    /// <returns>The native tab hover checks and optional frame captures.</returns>
    [Theory]
    [InlineData(false, "conversation")]
    [InlineData(true, "conversation")]
    [InlineData(false, "workbench")]
    [InlineData(true, "workbench")]
    [InlineData(false, "settings-ext")]
    [InlineData(true, "settings-ext")]
    public Task TabsKeepTheirThemeColorsThroughHover(bool dark, string scene) => WithSettingsAsync(
        scene,
        async (window, reads) =>
        {
            await WaitForMotionFrame(() => window.Classes.Contains("motion")).ConfigureAwait(true);
            window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            window.UpdateLayout();
            if (scene == "conversation")
            {
                var tabs = window.FindControl<SessionTabs>("PaneTabs")!;
                using var http = new HttpClient(reads) { BaseAddress = new Uri("http://127.0.0.1:4000") };
                var engine = new SessionEngine(new KernelClient(http), new SseEventTransport(http.BaseAddress.ToString(), http));
                var first = new SessionPaneState(engine, new SessionStatus { Label = "新会话", SessionPath = "/tmp/one.jsonl" });
                var second = new SessionPaneState(engine, new SessionStatus { Label = "新会话 2", SessionPath = "/tmp/two.jsonl" });
                tabs.Show(new[] { first, second }, first, InterfaceLanguage.Zh, new Dictionary<string, PeerTabMark>());
                window.UpdateLayout();
                var frames = tabs.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("pane-tab-frame")).ToArray();
                await Sample(frames[1], frames[1], "session-tab", "OverlayBrush").ConfigureAwait(true);
                await Sample(frames[0], frames[0], "session-current").ConfigureAwait(true);
                var recent = ControlAt<Button>(window, "action:session.recent");
                var recentSurface = recent.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "Frame");
                await Sample(recent, recentSurface, "rail-tab").ConfigureAwait(true);
                return;
            }

            if (scene == "workbench")
            {
                ClickAt(window, "action:workbench.new");
                ClickAt(window, "action:workbench.launch-browser");
                window.UpdateLayout();
                var tabs = window.FindControl<StackPanel>("FileTabs")!.Children.OfType<WorkbenchHeaderTab>();
                var file = tabs.Single(tab => Equals(tab.Tag, "README.md"));
                var browser = tabs.Single(tab => AutomationProperties.GetAutomationId(tab.PickButton) == "action:workbench.browser-tab");
                await Sample(file, file.FindControl<Border>("Frame")!, "file-tab", "OverlayBrush").ConfigureAwait(true);
                await WaitForMotionFrame(() => !browser.IsAnimating(Visual.OpacityProperty)).ConfigureAwait(true);
                var border = browser.FindControl<Border>("Frame")!;
                await Sample(browser, border, "browser-current").ConfigureAwait(true);
                ClickAt(window, "action:workbench.new");
                ClickAt(window, "action:workbench.launch-browser");
                window.UpdateLayout();
                browser = tabs.Single(tab => !tab.Classes.Contains("active") && AutomationProperties.GetAutomationId(tab.PickButton) == "action:workbench.browser-tab");
                await Sample(browser, browser.FindControl<Border>("Frame")!, "browser-tab", "OverlayBrush").ConfigureAwait(true);
                return;
            }

            var nav = window.FindControl<ListBox>("SettingsNav")!.GetVisualDescendants().OfType<ListBoxItem>().First(item => !item.IsSelected);
            await Sample(nav, nav, "settings-tab", "OverlayBrush").ConfigureAwait(true);
            var market = window.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("ext-switch-tab") && Equals(button.Tag, "market"));
            await Sample(market, market, "extension-tab").ConfigureAwait(true);

            static (Color Color, double Alpha) Paint(IBrush? value)
            {
                var brush = Assert.IsAssignableFrom<ISolidColorBrush>(value);
                return (Color.FromRgb(brush.Color.R, brush.Color.G, brush.Color.B), brush.Color.A * brush.Opacity / 255);
            }

            async Task Sample(Control target, Control surface, string stem, string? hoverResource = null)
            {
                var background = surface is Border ? Border.BackgroundProperty : Button.BackgroundProperty;
                var outside = new Point(700, 80);
                window.MouseMove(outside);
                await WaitForMotionFrame(() => !surface.IsAnimating(background)).ConfigureAwait(true);
                var rest = Paint(surface.GetValue(background));
                var samples = new List<(Color Color, double Alpha)>();
                surface.PropertyChanged += (_, change) =>
                {
                    if (change.Property == background && surface.GetValue(background) is ISolidColorBrush brush)
                    {
                        samples.Add(Paint(brush));
                    }
                };

                var inside = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
                Capture("rest");
                window.MouseMove(inside);
                Assert.True(target.IsPointerOver, stem);
                await Task.Delay(35).ConfigureAwait(true);
                Capture("enter");
                await WaitForMotionFrame(() => !surface.IsAnimating(background)).ConfigureAwait(true);
                var hover = Paint(surface.GetValue(background));
                Capture("hover");
                CheckSamples();
                samples.Clear();
                window.MouseMove(outside);
                await Task.Delay(35).ConfigureAwait(true);
                Capture("exit");
                await WaitForMotionFrame(() => !surface.IsAnimating(background)).ConfigureAwait(true);
                Assert.Equal(rest, Paint(surface.GetValue(background)));
                CheckSamples();
                Capture("restored");

                void CheckSamples()
                {
                    if (hoverResource is null)
                    {
                        Assert.Equal(rest, hover);
                        Assert.All(samples, sample => Assert.Equal(rest, sample));
                        return;
                    }

                    Assert.True(window.TryFindResource(hoverResource, window.ActualThemeVariant, out var resource));
                    Assert.Equal(Paint(Assert.IsAssignableFrom<IBrush>(resource)), hover);
                    Assert.Contains(samples, sample => sample.Alpha is > 0 and < 1);
                    Assert.All(samples, sample => Assert.Equal(hover.Color, sample.Color));
                }

                void Capture(string phase) => CaptureMotionRegion(
                    window,
                    target,
                    -4,
                    4,
                    (int)Math.Ceiling(target.Bounds.Width) + 8,
                    (int)Math.Ceiling(target.Bounds.Height) + 8,
                    $"button-{stem}-{(dark ? "dark" : "light")}-{phase}");
            }
        },
        allowMotion: true);
}
