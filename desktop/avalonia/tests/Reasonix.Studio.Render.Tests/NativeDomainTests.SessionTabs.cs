using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Kernel.Events;
using Reasonix.Studio.Kernel.Session;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Session;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Native tab input preserves pane identities, activity and explicit menu targets.</summary>
    /// <returns>The native pointer, keyboard and menu interaction.</returns>
    [Fact]
    public async Task SessionTabsDispatchNativeFocusCloseRenameAndMenus()
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(RasterBuilder));
        await session.Dispatch(
            () =>
            {
                using var reads = new ReferenceReads();
                using var http = new HttpClient(reads) { BaseAddress = new Uri("http://127.0.0.1:4000") };
                var firstEngine = new SessionEngine(new KernelClient(http), new SseEventTransport(http.BaseAddress.ToString(), http));
                var secondEngine = new SessionEngine(new KernelClient(http), new SseEventTransport(http.BaseAddress.ToString(), http));
                firstEngine.RestoreRunning(true, firstEngine.Generation.Revision);
                var first = new SessionPaneState(firstEngine, new SessionStatus { Label = "Running pane", WorkspaceRoot = "/a", SessionPath = "/a/one.jsonl" });
                var second = new SessionPaneState(secondEngine, new SessionStatus { Label = "Unread pane", WorkspaceRoot = "/b", SessionPath = "/b/two.jsonl" }) { Unread = true };
                var tabs = new SessionTabs();
                var window = new Window { Content = tabs, Width = 760, Height = 480 };
                var focused = new List<SessionPaneState>();
                var closed = new List<SessionPaneState[]>();
                var renamed = new List<SessionPaneState>();
                tabs.FocusRequested += focused.Add;
                tabs.CloseRequested += closed.Add;
                tabs.RenameRequested += renamed.Add;
                window.Show();
                try
                {
                    tabs.Show(new[] { first, second }, first, InterfaceLanguage.En);
                    Dispatcher.UIThread.RunJobs();
                    var buttons = tabs.GetVisualDescendants().OfType<Button>().Where(button => AutomationProperties.GetAutomationId(button) == "action:pane.activate").ToArray();
                    Assert.InRange(buttons[0].Bounds.Width, 132, 216);
                    var firstClose = Assert.IsType<StackPanel>(buttons[0].Parent).Children.OfType<Button>()
                        .Single(button => AutomationProperties.GetAutomationId(button) == "action:pane.close");
                    var secondClose = Assert.IsType<StackPanel>(buttons[1].Parent).Children.OfType<Button>()
                        .Single(button => AutomationProperties.GetAutomationId(button) == "action:pane.close");
                    Assert.Equal(1, firstClose.Opacity);
                    Assert.Equal(0, secondClose.Opacity);
                    Assert.Contains(buttons[0].GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "●");
                    Assert.Contains(buttons[1].GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "•");
                    var point = buttons[1].TranslatePoint(new Point(40, 12), window)!.Value;
                    window.MouseMove(point);
                    window.MouseDown(point, MouseButton.Left);
                    window.MouseUp(point, MouseButton.Left);
                    Assert.Same(second, Assert.Single(focused));
                    buttons[1].Focus();
                    window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
                    window.KeyRelease(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
                    Assert.Same(first, focused[^1]);
                    var menu = Assert.IsType<MenuFlyout>(buttons[1].ContextFlyout);
                    var commands = menu.Items.OfType<MenuItem>().ToArray();
                    commands[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    Assert.Same(second, Assert.Single(renamed));
                    commands[2].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    Assert.Same(first, Assert.Single(Assert.Single(closed)));
                    commands[3].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    Assert.Equal(new[] { first, second }, closed[^1]);
                    tabs.GetVisualDescendants().OfType<Button>().Last(button => AutomationProperties.GetAutomationId(button) == "action:pane.close").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Same(second, Assert.Single(closed[^1]));
                    tabs.Show(new[] { first }, first, InterfaceLanguage.En);
                    Assert.False(tabs.IsVisible);
                }
                finally
                {
                    window.Close();
                }
            },
            default);
    }
}
