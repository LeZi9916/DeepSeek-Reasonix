using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Tests the saved URL exactly once when a click also causes a pending blur save.</summary>
    /// <returns>The focus, save, and probe interaction.</returns>
    [Fact]
    public Task SafetyProbeWaitsForAddressSave() => WithSettings("conversation", (w, reads) =>
    {
        var state = reads.Read<JsonObject>("/web-access-settings");
        state["search_backends"] = new JsonArray("searxng");
        var save = new TaskCompletionSource<HttpResponseMessage>();
        var probes = 0;
        var saves = 0;
        reads.Exchange = request =>
        {
            if (request.RequestUri!.AbsolutePath == "/web-access-settings/probe")
            {
                probes++;
                return Task.FromResult<HttpResponseMessage?>(JsonReply(new JsonObject { ["ok"] = true }));
            }

            if (request.RequestUri.AbsolutePath != "/web-access-settings")
            {
                return Task.FromResult<HttpResponseMessage?>(null);
            }

            if (request.Method == HttpMethod.Get)
            {
                return Task.FromResult<HttpResponseMessage?>(JsonReply(state));
            }

            saves++;
            return WaitForSaveAsync();
        };
        Navigate(w, "settings-tools");
        var url = ControlAt<TextBox>(w, "field:web-access.searxng-url");
        var probe = ControlAt<Button>(w, "action:web-access.probe");
        url.BringIntoView();
        url.Focus();
        url.Text = "http://127.0.0.1:8080";
        Dispatcher.UIThread.RunJobs();
        Assert.True(url.IsFocused);
        probe.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, saves);
        Assert.True(probe.IsEffectivelyEnabled);
        probe.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, probes);
        state["searxng_url"] = url.Text;
        save.SetResult(JsonReply(state));
        Assert.True(SpinWait.SpinUntil(
            () =>
            {
                Dispatcher.UIThread.RunJobs();
                return probes == 1;
            },
            TimeSpan.FromSeconds(3)));
        Assert.Equal(1, saves);

        async Task<HttpResponseMessage?> WaitForSaveAsync() => await save.Task.ConfigureAwait(false);
    });

    /// <summary>Exercises safety execution and web settings using kernel-shaped fixture replies.</summary>
    /// <param name="width">Reference width.</param>
    /// <param name="height">Reference height.</param>
    /// <param name="dark">Whether the reference uses dark mode.</param>
    /// <returns>The rendered settings interactions.</returns>
    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(1440, 900, true)]
    [InlineData(760, 480, false)]
    [InlineData(760, 480, true)]
    public Task SafetyExecutionAndWebMatchReference(int width, int height, bool dark) => WithSettings("conversation", (w, reads) =>
    {
        w.Width = width;
        w.Height = height;
        w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var states = new JsonObject();
        foreach (var path in new[] { "/sandbox", "/workspace/roots", "/shell", "/write-lease", "/web-access-settings", "/browser-tools" })
        {
            states[path] = reads.Read<JsonObject>(path).DeepClone();
        }

        var probes = 0;
        reads.Exchange = async request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get)
            {
                return states[path] is JsonObject current ? JsonReply(current) : null;
            }

            var fields = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!.AsObject();
            if (path == "/web-access-settings/probe")
            {
                probes++;
                return JsonReply(new JsonObject { ["ok"] = true });
            }

            if (states[path] is not JsonObject state)
            {
                if (path is "/workspace/roots/remove")
                {
                    var roots = states["/workspace/roots"]!.AsObject();
                    roots["extra"] = new JsonArray(roots["extra"]!.AsArray().Where(item => (string?)item != (string?)fields["path"]).Select(item => item!.DeepClone()).ToArray());
                    return JsonReply(roots);
                }

                return null;
            }

            if (path == "/workspace/roots")
            {
                state["extra"]!.AsArray().Add(fields["path"]!.DeepClone());
                return JsonReply(state);
            }

            foreach (var (key, value) in fields)
            {
                state[key] = value?.DeepClone();
            }

            if (path == "/shell")
            {
                state["effective"] = state["options"]!.AsArray().Single(item => (string?)item?["prefer"] == (string?)fields["prefer"])!.DeepClone();
            }

            if (path == "/browser-tools")
            {
                state["effective"] = false;
            }

            return JsonReply(state);
        };

        Navigate(w, "settings-tools");
        Capture("sandbox", "settings.sandbox");
        Capture("sandbox-command", "safety.how_commands_run");
        var extra = ControlAt<TextBox>(w, "field:sandbox.add-write-root");
        extra.Text = "/tmp/other";
        Dispatcher.UIThread.RunJobs();
        ClickAt(w, "action:sandbox.add-write-root");
        Assert.Contains(states["/sandbox"]!["allowWrite"]!.AsArray(), item => (string?)item == "/tmp/other");
        var folder = ControlAt<TextBox>(w, "field:folders.add");
        folder.Text = "/tmp/project-fixture";
        Dispatcher.UIThread.RunJobs();
        ClickAt(w, "action:folders.add");
        Assert.Single(states["/workspace/roots"]!["extra"]!.AsArray());
        ClickAt(w, "action:folders.remove");
        Assert.Empty(states["/workspace/roots"]!["extra"]!.AsArray());

        Capture("shell", "common.which_program_runs_commands");
        ClickAt(w, "action:shell.custom");
        Capture("shell-custom", "app.point_at_an_executable");
        Capture("shell-custom-tail", ControlAt<TextBox>(w, "field:shell.custom-args"));
        ClickAt(w, "action:shell.custom");
        var executors = w.GetLogicalDescendants().OfType<WrapPanel>().First(panel => panel.Name == "Executors");
        executors.Children.OfType<RadioButton>().Single(button => Equals(button.Tag, "pwsh")).IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("pwsh", (string?)states["/shell"]!["prefer"]);
        Assert.Equal(string.Empty, (string?)states["/shell"]!["path"]);

        Capture("write-lease", "session.several_sessions_writing_one_project");
        var wait = ControlAt<TextBox>(w, "field:write-lease.max-wait");
        wait.Text = "45";
        wait.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(45, (int?)states["/write-lease"]!["maxWaitMinutes"]);

        Capture("web", "safety.web");
        Capture("web-default", "models.web_search_and_fetch");
        var web = states["/web-access-settings"]!.AsObject();
        web["search_backends"] = new JsonArray("searxng", "deepseek_api");
        web["fetch_backends"] = new JsonArray("local", "deepseek_api");
        web["searxng_url"] = "http://127.0.0.1:8080";
        web["video_url_passthrough"] = true;
        web["deepseek_api_ready"] = true;
        ReloadSettings();
        Capture("web-backends", "models.web_search_and_fetch");
        Capture("web-fetch", "models.web_fetch");
        Capture("web-video", "models.video_addresses");
        Capture("web-searxng", ControlAt<TextBox>(w, "field:web-access.searxng-url"));

        web["deepseek_api_ready"] = false;
        web["video_url_passthrough"] = false;
        ReloadSettings();
        Capture("web-no-paid", "common.web_search");
        var input = ControlAt<TextBox>(w, "field:web-access.searxng-url");
        input.Text = "http://127.0.0.1:8888";
        Dispatcher.UIThread.RunJobs();
        ClickAt(w, "action:web-access.probe");
        Assert.Equal(1, probes);
        Assert.Equal("http://127.0.0.1:8888", (string?)states["/web-access-settings"]!["searxng_url"]);

        Target<ToggleSwitch>("action:web-access.backend", "fetch:deepseek_api").IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Target<ToggleSwitch>("action:web-access.backend", "fetch:deepseek_api").IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(states["/web-access-settings"]!["fetch_backends"]!.AsArray(), node => (string?)node == "deepseek_api");
        Assert.Empty(states["/web-access-settings"]!["fetch_paid_allowed"]!.AsArray());
        Assert.False(Target<ToggleSwitch>("action:web-access.consent", "fetch:deepseek_api").IsEffectivelyEnabled);
        Capture("browser-tools", "common.built_in_browser");
        var browser = ControlAt<ToggleSwitch>(w, "action:browser-tools.enabled");
        browser.IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        browser.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True((bool?)states["/browser-tools"]!["enabled"]);
        Assert.False((bool?)states["/browser-tools"]!["effective"]);

        void ReloadSettings()
        {
            var nav = w.FindControl<ListBox>("SettingsNav")!;
            var current = nav.SelectedItem;
            nav.SelectedIndex = 0;
            Dispatcher.UIThread.RunJobs();
            nav.SelectedItem = current;
            Dispatcher.UIThread.RunJobs();
        }

        T Target<T>(string action, string tag)
            where T : Control => w.GetVisualDescendants().OfType<T>().Single(control => AutomationProperties.GetAutomationId(control) == action && Equals(control.Tag, tag));

        void Capture(string phase, object tag)
        {
            var target = tag as Control ?? w.GetVisualDescendants().OfType<Control>().First(control => Equals(control.Tag, tag) && control.IsEffectivelyVisible);
            var scroll = w.FindControl<ScrollViewer>("SettingsScroller")!;
            scroll.Offset = new Vector(0, Math.Max(0, scroll.Offset.Y + target.TranslatePoint(default, w)!.Value.Y - (width >= 1000 ? 120 : 151)));
            Dispatcher.UIThread.RunJobs();
            CaptureFlow(w, $"native-safety-{phase}-{width}x{height}", resetScroll: false);
            if (phase.StartsWith("browser-", StringComparison.Ordinal))
            {
                CaptureFlow(w, $"native-safety-{phase}-{width}x{height}-panel", resetScroll: false, region: target);
            }
        }
    });
}
