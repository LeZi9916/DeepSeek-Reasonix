using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Shell.Workspace;
using Reasonix.Studio.State;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Exercises the operations settings and the controls they change in the workspace.</summary>
    /// <param name="width">Window width.</param>
    /// <param name="height">Window height.</param>
    /// <param name="dark">Whether the fixture uses the dark theme.</param>
    /// <returns>The settings and live workspace interaction.</returns>
    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(1440, 900, true)]
    [InlineData(760, 480, false)]
    [InlineData(760, 480, true)]
    public Task OpsSettingsAffectTheWorkspace(int width, int height, bool dark) => WithSettings("conversation", (w, reads) =>
    {
        w.Width = width;
        w.Height = height;
        w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var stops = 0;
        reads.Exchange = request =>
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/cancel")
            {
                stops++;
                return Task.FromResult<HttpResponseMessage?>(new HttpResponseMessage(HttpStatusCode.NoContent));
            }

            if (request.RequestUri!.AbsolutePath == "/inbox")
            {
                return Task.FromResult<HttpResponseMessage?>(JsonReply(System.Text.Json.Nodes.JsonNode.Parse("""
                    {"revision":1,"paused":false,"items":[{"id":"fixture-1","preview":"排队的草稿 1","intent":"followup","state":"queued"},{"id":"fixture-2","preview":"排队的草稿 2","intent":"followup","state":"queued"}],"capacity":{}}
                    """)!.AsObject()));
            }

            return Task.FromResult<HttpResponseMessage?>(null);
        };

        Navigate(w, "settings-controls");
        Capture("keyboard", "settings.keyboard_shortcuts");
        var keys = w.GetVisualDescendants().OfType<Control>().Count(control => AutomationProperties.GetAutomationId(control) == "class:keys-row");
        Assert.Equal(15, keys);
        Capture("keys", Named("KeyboardRows").GetVisualAncestors().OfType<Reasonix.Studio.Shell.Settings.KeyboardShortcutSheet>().First());
        Capture("keys-tail", Named("KeyboardRows").GetVisualDescendants().OfType<Border>()
            .Where(control => AutomationProperties.GetAutomationId(control) == "class:keys-row").ElementAt(9));
        Capture("esc-stop", "safety.esc_stops_a_task");
        Assert.True(Option("action:appearance.esc-stop", "confirm").IsChecked);
        Set("action:appearance.esc-stop", "now");
        Capture("esc-now", "safety.esc_stops_a_task");
        Set("action:appearance.esc-stop", "confirm");

        Capture("navigation", "settings.tabs_and_navigation_card");
        var tabs = ControlAt<ToggleSwitch>(w, "action:appearance.session-tabs");
        Assert.False(tabs.IsChecked);
        Capture("session-tabs", "session.session_tabs");
        tabs.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Capture("session-tabs-on", "session.session_tabs");
        tabs.IsChecked = false;
        Capture("session-quick", "session.session_row_buttons");
        Set("action:appearance.session-quick-actions", "on");
        Assert.True(w.FindControl<Reasonix.Studio.Pages.Rail.ProjectRail>("Projects")!.QuickActions);
        Capture("session-quick-on", "session.session_row_buttons");
        Set("action:appearance.session-quick-actions", "off");
        Capture("navrail", "settings.icon_rail");
        Set("action:appearance.nav-rail", "collapsed");
        Capture("navrail-collapsed", "settings.icon_rail");
        Set("action:appearance.nav-rail", "on");
        Assert.True(w.FindControl<Control>("NavigationRail")!.IsVisible);
        Set("action:appearance.nav-rail", "off");
        Capture("naventries", "settings.navigation_entries");
        Find<Button>("action:appearance.nav-entry-move", "usage:down").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var hide = Find<ToggleSwitch>("action:appearance.nav-entry-visible", "files");
        hide.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(w.FindControl<StackPanel>("QuickLinksRows")!.Children.OfType<Button>(), button => Equals(button.Tag, "files"));
        Capture("naventries-edited", "settings.navigation_entries");
        Capture("naventries-tail", ControlAt<Button>(w, "action:appearance.nav-entries-reset"));
        ClickAt(w, "action:appearance.nav-entries-reset");
        Assert.True(Find<ToggleSwitch>("action:appearance.nav-entry-visible", "files").IsChecked);
        Capture("live-grace", "settings.keep_finished_sessions_in_running");
        Set("action:appearance.live-grace", "30");
        Capture("live-grace-30", "settings.keep_finished_sessions_in_running");

        Capture("running", "settings.while_running_card");
        Capture("running-intent", "settings.messages_sent_while_running");
        Set("action:composer.running-message-intent", "followup");
        Assert.True(w.QueueDuringRun);
        Capture("running-followup", "settings.messages_sent_while_running");
        Capture("composer-hint", "composer.composer_hint");
        Set("action:appearance.composer-hint", "off");
        Assert.Equal(string.Empty, w.FindControl<TextBox>("Composer")!.PlaceholderText);
        Capture("composer-hint-off", "composer.composer_hint");
        Set("action:appearance.composer-hint", "on");
        Capture("performance", "settings.performance_card");
        var open = ControlAt<TextBox>(w, "field:appearance.open-session-limit");
        Capture("open-limit", "settings.session_tabs_kept_open");
        Commit(open, "12");
        Assert.Equal("12", open.Text);
        Capture("open-limit-draft", "settings.session_tabs_kept_open");
        var live = ControlAt<TextBox>(w, "field:appearance.live-stream-budget");
        Capture("stream-budget", "settings.session_tabs_kept_live");
        Commit(live, "3");
        Assert.Equal("3", live.Text);
        Capture("stream-budget-draft", "settings.session_tabs_kept_live");
        Capture("page-sleep", "settings.idle_page_sleep");
        Set("action:appearance.page-sleep", "on");
        Capture("page-sleep-on", "settings.idle_page_sleep");
        Set("action:composer.running-message-intent", "steer");

        CloseSettings();
        w.AdoptComposer(reads.Read<SessionStatus>("/status"));
        Navigate(w, "workbench");
        var primary = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
        w.KeyPress(Key.K, primary, PhysicalKey.K, "k");
        w.KeyRelease(Key.K, primary, PhysicalKey.K, "k");
        Dispatcher.UIThread.RunJobs();
        Capture("workspace-keys");
        Find<Button>("action:palette.scope", "actions").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Capture("workspace-actions");
        w.GetVisualDescendants().OfType<WorkspaceSearchPanel>().Single().Hide();
        if (!w.FindControl<Control>("Rail")!.IsEffectivelyVisible)
        {
            w.FindControl<Button>("ExpandRail")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }

        w.FindControl<Button>("OpenAccount")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Capture("workspace-shortcuts");
        w.FindControl<Button>("OpenAccount")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        if (width == 760 && w.FindControl<Control>("Rail")!.IsEffectivelyVisible)
        {
            w.FindControl<Button>("CollapseRail")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }

        var running = Reasonix.Studio.Testing.ReferenceConversation.Create() with
        {
            Running = true,
        };
        w.ShowState(running);
        w.FindControl<TextBox>("Composer")!.Text = "只用于界面对照";
        var queue = w.RefreshQueueAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.True(queue.IsCompletedSuccessfully);
        var runBadge = w.FindControl<Border>("ComposerRunBadge")!;
        Assert.True(runBadge.IsEffectivelyVisible);
        Assert.Equal("运行中", AutomationProperties.GetName(runBadge));
        var compose = w.FindControl<Control>("ComposeSurface")!;
        var queueScroll = w.FindControl<ScrollViewer>("QueueScroll")!;
        Assert.Contains(compose, queueScroll.GetVisualAncestors());
        Assert.True(queueScroll.IsEffectivelyVisible);
        var firstQueueRow = w.FindControl<StackPanel>("QueueStrip")!.Children[0];
        var queueActions = firstQueueRow.GetVisualDescendants().OfType<Button>().ToArray();
        Assert.Equal(new[] { "改为插话", "立即发送", "撤回" }, queueActions.Select(action => action.Content));
        foreach (var action in queueActions)
        {
            var position = action.TranslatePoint(default, queueScroll)!.Value;
            Assert.InRange(position.X + action.Bounds.Width, 1, queueScroll.Bounds.Width);
            Assert.InRange(position.Y + action.Bounds.Height, 1, queueScroll.Bounds.Height);
        }

        Capture("workspace-steer");
        w.QueueDuringRun = true;
        w.ShowState(running);
        Dispatcher.UIThread.RunJobs();
        if (width == 760)
        {
            var send = w.FindControl<Button>("Send")!;
            Assert.InRange(send.TranslatePoint(default, compose)!.Value.X + send.Bounds.Width, 0, compose.Bounds.Width);
            Assert.True(w.FindControl<TextBlock>("SendIntent")!.IsVisible);
        }

        Capture("workspace-followup");
        w.FindControl<Button>("ViewSwitch")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Escape();
        Assert.Equal(0, stops);
        Assert.True(w.FindControl<Border>("StopHint")!.IsVisible);
        Capture("workspace-esc-armed");
        Escape();
        Assert.Equal(1, stops);
        Assert.False(w.FindControl<Border>("StopHint")!.IsVisible);

        void Set(string id, string value)
        {
            Option(id, value).IsChecked = true;
            Dispatcher.UIThread.RunJobs();
        }

        void CloseSettings()
        {
            w.FindControl<Button>("CloseSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }

        RadioButton Option(string id, string value) => Find<RadioButton>(id, value);

        T Find<T>(string id, string value)
            where T : Control => w.GetVisualDescendants().OfType<T>().Single(control =>
                control.IsEffectivelyVisible && AutomationProperties.GetAutomationId(control) == id && Equals(control.Tag, value));

        Control Named(string name) => w.GetVisualDescendants().OfType<Control>().Single(control => control.Name == name);

        void Escape()
        {
            w.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Dispatcher.UIThread.RunJobs();
        }

        void Capture(string phase, object? target = null)
        {
            if (target is not null)
            {
                var element = target as Control ?? w.GetVisualDescendants().OfType<Control>()
                    .First(control => control.IsEffectivelyVisible && Equals(control.Tag, target));
                var scroll = w.FindControl<ScrollViewer>("SettingsScroller")!;
                var y = element.TranslatePoint(default, w)!.Value.Y;
                scroll.Offset = new Vector(0, Math.Max(0, scroll.Offset.Y + y - (width >= 1000 ? 151 : 215)));
                Dispatcher.UIThread.RunJobs();
            }

            CaptureFlow(w, $"native-ops-{phase}-{width}x{height}", resetScroll: false);
        }

        static void Commit(TextBox input, string value)
        {
            input.Text = value;
            input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Dispatcher.UIThread.RunJobs();
        }
    });

    /// <summary>Checks the shortcut table's reference row pitch.</summary>
    /// <returns>The controls layout check.</returns>
    [Fact]
    public Task KeyboardShortcutRowsKeepTheReferencePitch() => WithSettings("settings-controls", (w, _) =>
    {
        var rows = w.GetVisualDescendants().OfType<Border>()
            .Where(border => AutomationProperties.GetAutomationId(border) == "class:keys-row").ToArray();
        Assert.Equal(15, rows.Length);
        double Y(Control control) => control.TranslatePoint(default, w)!.Value.Y;
        Assert.InRange(Y(rows[0]), 368.2, 369.2);
        Assert.InRange(Y(rows[^1]), 648.2, 649.2);
        var cards = w.GetVisualDescendants().OfType<Border>()
            .Where(border => AutomationProperties.GetAutomationId(border) == "class:prefs-card").ToArray();
        Assert.InRange(cards[0].Bounds.Height, 469, 471);
        Assert.InRange(Y(cards[1]), 747.6, 749.6);
    });
}
