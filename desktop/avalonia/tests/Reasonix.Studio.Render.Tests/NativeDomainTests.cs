using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Baseline;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Kernel.Events;
using Reasonix.Studio.Kernel.Session;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Pages.Rail;
using Reasonix.Studio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Reasonix.Studio.Render.Tests;

/// <summary>Opens each indexed reference screen through the native window and compares its visible tree.</summary>
public sealed partial class NativeDomainTests
{
    /// <summary>Per-case gate output.</summary>
    private readonly ITestOutputHelper _output;

    /// <summary>Initializes a new instance of the <see cref="NativeDomainTests"/> class.</summary>
    /// <param name="output">Receives the measured differences.</param>
    public NativeDomainTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>Gets the domains actually captured by the reference renderer.</summary>
    public static IEnumerable<object[]> Domains
    {
        get
        {
            var folder = Path.Combine(AppContext.BaseDirectory, "baseline");
            var index = JsonSerializer.Deserialize<BaselineIndex>(File.ReadAllText(Path.Combine(folder, "index.json")))!;
            var responsive = Directory.GetFiles(Path.Combine(folder, "responsive"), "*.json");
            var expected = index.Domains.Select(d => Path.GetFileNameWithoutExtension(d.File).Split('.'))
                .Select(parts => $"{parts[0]}-760x480.{parts[1]}.json");
            Assert.All(expected, name => Assert.Contains(Path.Combine(folder, "responsive", name), responsive));
            return index.Domains.Select(domain => domain.File).Concat(responsive.Select(path => Path.Combine("responsive", Path.GetFileName(path)))).Select(file => new object[] { file });
        }
    }

    /// <summary>Checks screen arrival and reports the geometry and styles still owed on that screen.</summary>
    /// <param name="file">Reference domain file.</param>
    /// <returns>The native render and comparison.</returns>
    [Theory]
    [MemberData(nameof(Domains))]
    public async Task NamedScreenRenders(string file)
    {
        var domain = JsonSerializer.Deserialize<BaselineDomain>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "baseline", file)))!;
        var responsive = file.StartsWith("responsive", StringComparison.Ordinal);
        file = Path.GetFileName(file);
        var target = file.Split('.')[0];
        if (responsive)
        {
            target = target[..target.LastIndexOf('-')];
        }

        var output = Environment.GetEnvironmentVariable("STUDIO_PARITY_OUTPUT");
        var session = HeadlessUnitTestSession.StartNew(typeof(RasterBuilder));
        await session.Dispatch(
            () =>
            {
                using var handler = new ReferenceReads();
                using var http = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:4000") };
                var engine = new SessionEngine(new KernelClient(http), new SseEventTransport(http.BaseAddress.ToString(), http));
                var window = new MainWindow(false, engine, InterfaceLanguage.Zh, _ => Task.FromResult(false))
                {
                    Width = domain.Viewport.Width,
                    Height = domain.Viewport.Height,
                    RequestedThemeVariant = file.Contains(".dark.", StringComparison.Ordinal) ? ThemeVariant.Dark : ThemeVariant.Light,
                };
                window.RestoreChrome(264, file.Contains(".dark.", StringComparison.Ordinal) ? "Dark" : "Light", false);
                window.Show();
                try
                {
                    window.AdoptComposer(handler.Read<SessionStatus>("/status"));
                    window.FindControl<ProjectRail>("Projects")!.Show(handler.Read<TreeWorkspace[]>("/tree"), null);
                    window.ShowState(Reasonix.Studio.Testing.ReferenceConversation.Create());
                    var region = Navigate(window, target);
                    Dispatcher.UIThread.RunJobs();
                    var settingsError = window.FindControl<TextBlock>("SettingsError")!.Text;
                    Assert.True(string.IsNullOrEmpty(settingsError), settingsError);
                    Assert.True(region.IsEffectivelyVisible && region.Bounds.Width > 0, $"{target} {region.Bounds}");
                    var tree = new GeometryExporter(GeometryComparerOptions.Studio).Export(window, region);
                    var findings = new GeometryComparer(GeometryComparerOptions.Studio).Compare(domain, tree);
                    var mapped = tree.Keys.Count(key => !key.StartsWith("token:", StringComparison.Ordinal));
                    var anchor = target switch
                    {
                        "conversation" => responsive ? "class:chrome#0" : "class:compose#0",
                        "workbench" => "class:workbench#0",
                        "onboarding" => "class:onb-stage#0",
                        "analysis" => "class:chrome#0",
                        _ => "class:prefs-sheet#0",
                    };
                    Assert.True(tree.ContainsKey(anchor), file + ": " + anchor);
                    Assert.DoesNotContain(findings, issue => issue.Identity == anchor && issue.Kind is "box" or "missing");
                    var pageAnchors = target switch
                    {
                        "workbench" => new[] { "class:workbench-tabs#0", "class:workbench-body#0", "class:workbench-explorer#0", "class:workbench-canvas#0" },
                        "analysis" => new[] { "class:run-analysis-head#0", "class:run-kpis#0" },
                        "onboarding" when domain.Viewport.Width < 900 => new[] { "class:onb-card#0", "class:onb-foot#0" },
                        "onboarding" => new[] { "class:onb-card#0", "class:onb-demo#0", "class:onb-foot#0" },
                        "conversation" => Array.Empty<string>(),
                        _ => new[] { "class:prefs-find#0", "role:tablist#0", "role:tabpanel#0", "action:settings.section#0" },
                    };
                    foreach (var key in pageAnchors)
                    {
                        Assert.True(tree.ContainsKey(key), file + ": " + key);
                        if (responsive && key == "action:settings.section#0")
                        {
                            // Named reference captures start at the first tab. Keyboard navigation scrolls the active tab into view.
                            var nav = window.FindControl<ListBox>("SettingsNav")!;
                            var offset = nav.GetVisualDescendants().OfType<ScrollViewer>().Single().Offset.X;
                            var expected = domain.Nodes.First(node => GeometryComparer.NodeKey(node) == key).Box;
                            var observed = tree[key];
                            var tolerance = GeometryComparerOptions.Studio.PixelTolerance;
                            Assert.InRange(Math.Abs(observed.X + offset - expected.X), 0, tolerance);
                            Assert.InRange(Math.Abs(observed.Y - expected.Y), 0, tolerance);
                            Assert.InRange(Math.Abs(observed.Width - expected.Width), 0, tolerance);
                            Assert.InRange(Math.Abs(observed.Height - expected.Height), 0, tolerance);
                        }
                        else
                        {
                            Assert.DoesNotContain(findings, issue => issue.Identity == key && issue.Kind is "box" or "missing");
                        }
                    }

                    if (target == "conversation")
                    {
                        foreach (var key in new[] { "class:compose#0", "role:combobox#0", "action:session.send#0" })
                        {
                            Assert.True(tree.ContainsKey(key), file + ": " + key);
                            Assert.DoesNotContain(findings, issue => issue.Identity == key && issue.Kind is "box" or "missing");
                        }
                    }

                    if (target == "workbench")
                    {
                        var gitIds = new[] { "class:workbench-changes#0", "class:workbench-history#0", "action:workbench.stage#0", "action:workbench.discard#0", "action:workbench.commit#0", "action:commit.draft#0", "action:push.preview#0" };
                        Assert.All(gitIds, id => Assert.True(tree.ContainsKey(id), id));
                        Assert.DoesNotContain(findings, f => f.Kind == "box" && IsWorkbenchGeometry(f.Identity));
                    }

                    _output.WriteLine($"{file}: {mapped} observed nodes, {findings.Count} differences");

                    if (!string.IsNullOrEmpty(output))
                    {
                        Directory.CreateDirectory(output);
                        using var frame = new RenderTargetBitmap(PixelSize.FromSize(window.ClientSize, 1), new Vector(96, 96));
                        frame.Render(window);
                        var stem = Path.Combine(output, Path.GetFileNameWithoutExtension(file));
                        frame.Save(stem + ".png", PngBitmapEncoderOptions.Default);
                        using var pixels = SkiaSharp.SKBitmap.Decode(stem + ".png");
                        var corner = pixels.GetPixel(0, 0);
                        var painted = Enumerable.Range(1, pixels.Height / 12).Any(y =>
                            Enumerable.Range(1, pixels.Width / 12).Any(x => pixels.GetPixel((x * 12) - 1, (y * 12) - 1) != corner));
                        Assert.True(painted, file);
                        var report = new
                        {
                            domain = file,
                            nodes = domain.Nodes.Count,
                            observed = mapped,
                            findings,
                        };
                        File.WriteAllText(stem + ".json", JsonSerializer.Serialize(report));
                    }

                    if (target == "workbench" && output is not null)
                    {
                        GitScreenProofs(window, output, file);
                    }

                    if (target == "onboarding")
                    {
                        window.FindControl<Button>("BeginSetup")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        var services = window.FindControl<WrapPanel>("SetupServices");
                        Assert.NotNull(services);
                        window.FindControl<TextBox>("ServiceKey")!.Text = "local-only-test";
                        services.Children.OfType<RadioButton>().Last().IsChecked = true;
                        Assert.True(string.IsNullOrEmpty(window.FindControl<TextBox>("ServiceAddress")!.Text));
                        Assert.True(string.IsNullOrEmpty(window.FindControl<TextBox>("ServiceKey")!.Text));
                        Assert.False(window.FindControl<Button>("ConnectProvider")!.IsEnabled);
                    }

                    if (target == "settings-appearance")
                    {
                        var sheet = window.FindControl<StackPanel>("SettingsBody")!;
                        var stage = window.FindControl<Grid>("SetupPanel")!;
                        var original = Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(stage.Background).Color;
                        Button Pick(string name) => sheet.GetVisualDescendants().OfType<Button>().Single(b =>
                            AutomationProperties.GetAutomationId(b) == "action:theme.activate" && AutomationProperties.GetName(b) == name);
                        Pick("Dusk").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Dispatcher.UIThread.RunJobs();
                        var dusk = handler.Read<JsonArray>("/themes").Single(p => (string?)p?["id"] == "dusk")!;
                        Assert.True((bool?)dusk["active"]);
                        var scheme = window.ActualThemeVariant == ThemeVariant.Dark ? "dark" : "light";
                        Assert.Equal(Avalonia.Media.Color.Parse((string)dusk["tokens"]![scheme]!["bg"]!), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(stage.Background).Color);
                        Pick(MessageCatalog.Get(MessageKey.Common.Default, InterfaceLanguage.Zh)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Dispatcher.UIThread.RunJobs();
                        Assert.Equal(original, Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(stage.Background).Color);
                    }

                    if (target == "settings-storage")
                    {
                        // Keep every previously observed box present while closing the original 32 discrepancies.
                        var required = new[] { "class:prefs-pagetitle#0", "class:prefs-col#0", "class:prefs-card#0", "class:prefs-card#1", "class:memgrid#0", "action:memory.proactive#0", "action:memory.search#0", "action:memory.confirm#0", "action:memory.edit#0", "action:memory.forget#0", "class:srow#0", "class:srow-name#0", "class:srow-desc#0" }
                            .Concat(Enumerable.Range(0, 12).Select(i => "action:memory.filter#" + i))
                            .Concat(Enumerable.Range(0, 7).Select(i => "action:memory.open#" + i)).ToArray();
                        Assert.All(required, key => Assert.True(tree.ContainsKey(key), key));
                        Assert.DoesNotContain(findings, issue => issue.Kind == "box");

                        var page = window.FindControl<StackPanel>("SettingsBody")!;
                        var search = page.GetVisualDescendants().OfType<TextBox>().Single(c => AutomationProperties.GetAutomationId(c) == "action:memory.search");
                        search.Text = "no-coauthored-by";
                        Dispatcher.UIThread.RunJobs();
                        Assert.Single(page.GetVisualDescendants().OfType<Button>(), c => AutomationProperties.GetAutomationId(c) == "action:memory.open");
                        search.Text = "absent-in-the-captured-memory";
                        Dispatcher.UIThread.RunJobs();
                        Assert.DoesNotContain(page.GetVisualDescendants().OfType<Button>(), c => AutomationProperties.GetAutomationId(c) == "action:memory.open");
                    }
                }
                finally
                {
                    window.Close();
                }
            },
            default);
    }

    /// <summary>Search opens the setting itself and cancelling departure preserves the unsaved persona.</summary>
    /// <returns>The interaction.</returns>
    [Fact]
    public async Task SearchAndDirtyPersonaUseTheNormalSheet()
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(RasterBuilder));
        await session.Dispatch(
            () =>
            {
                using var handler = new ReferenceReads();
                using var http = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:4000") };
                var engine = new SessionEngine(new KernelClient(http), new SseEventTransport(http.BaseAddress.ToString(), http));
                var window = new MainWindow(false, engine, InterfaceLanguage.En, _ => Task.FromResult(false));
                window.Show();
                try
                {
                    window.FindControl<Button>("OpenSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var query = window.FindControl<TextBox>("SettingsSearch")!;
                    query.Text = MessageCatalog.Get("persona.additional_instructions", InterfaceLanguage.En);
                    Dispatcher.UIThread.RunJobs();
                    var hits = window.FindControl<ListBox>("SettingsHits")!;
                    Assert.True(hits.IsEffectivelyVisible);
                    query.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Enter });
                    Dispatcher.UIThread.RunJobs();
                    var editor = window.FindControl<StackPanel>("SettingsBody")!.GetVisualDescendants().OfType<TextBox>().Last();
                    editor.Text = "unsaved edit";
                    window.FindControl<Button>("CloseSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(window.FindControl<Border>("SettingsLeavePrompt")!.IsEffectivelyVisible);
                    window.FindControl<Button>("KeepSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(editor.IsEffectivelyVisible);
                    Assert.Equal("unsaved edit", editor.Text);
                    window.FindControl<Button>("CloseSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    window.FindControl<Button>("DiscardSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Dispatcher.UIThread.RunJobs();
                    Assert.False(window.FindControl<Grid>("SettingsPanel")!.IsVisible);
                }
                finally
                {
                    window.Close();
                }
            },
            default);
    }

    /// <summary>Enters a named pane or settings section through its ordinary action.</summary>
    /// <param name="window">The real shell window.</param>
    /// <param name="target">Indexed reference screen.</param>
    /// <returns>The appropriate reference capture root.</returns>
    private static Control Navigate(MainWindow window, string target)
    {
        if (target == "conversation")
        {
            return window;
        }

        if (target is "analysis" or "workbench")
        {
            var name = target == "analysis" ? "AnalysisView" : "WorkbenchView";
            window.FindControl<MenuItem>(name)!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            if (target == "workbench")
            {
                var files = window.FindControl<ListBox>("WorkspaceFiles")!;
                files.SelectedItem = files.Items.Cast<object>().Single(f => f.ToString() == "README.md");
                Dispatcher.UIThread.RunJobs();
                var editor = window.FindControl<TextBox>("FileEditor")!;
                var preview = window.FindControl<ScrollViewer>("RenderedFile")!;
                Assert.NotEmpty(editor.Text!);
                Assert.True(preview.IsEffectivelyVisible);
                window.FindControl<Button>("EditFile")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.True(editor.IsEffectivelyVisible);
                window.FindControl<Button>("PreviewFile")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.True(preview.IsEffectivelyVisible);
            }

            Assert.True(window.FindControl<Control>(target == "analysis" ? "AnalysisPanel" : "WorkbenchPanel")!.IsEffectivelyVisible);
            return window;
        }

        window.FindControl<Button>("OpenSettings")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var nav = window.FindControl<ListBox>("SettingsNav")!;
        var labels = new Dictionary<string, string>
        {
            ["common"] = "settings.common",
            ["model"] = "common.models",
            ["session"] = "settings.agent_behaviour",
            ["tools"] = "settings.permissions_and_safety",
            ["ext"] = "ext.extension",
            ["display"] = "settings.conversation_display",
            ["controls"] = "settings.controls_and_performance",
            ["appearance"] = "settings.appearance",
            ["storage"] = "settings.data",
            ["network"] = "usage.connections",
            ["account"] = "settings.account_and_about",
            ["advanced"] = "settings.advanced",
        };
        var parts = target.Split('-');
        var section = target == "onboarding" ? "model" : parts[1];
        var title = MessageCatalog.Get(labels[section], InterfaceLanguage.Zh);
        nav.SelectedItem = nav.Items.Cast<object>().Single(item => item.ToString() == title);
        Dispatcher.UIThread.RunJobs();
        var body = window.FindControl<StackPanel>("SettingsBody")!;
        Assert.Equal(title, window.FindControl<TextBlock>("SettingsHeading")!.Text);
        if (target is "settings-session" or "settings-storage" or "settings-appearance")
        {
            var key = target switch
            {
                "settings-storage" => "action:memory.search",
                "settings-appearance" => "action:theme.activate",
                _ => "action:agent-preset.view",
            };
            Assert.Contains(body.GetVisualDescendants().OfType<Control>(), control => AutomationProperties.GetAutomationId(control) == key && control.IsEffectivelyVisible);
        }

        var action = target switch
        {
            "onboarding" => MessageKey.Onboarding.ConnectAModelService,
            "settings-ext-market" => MessageKey.Ext.Discover,
            "settings-session-persona" => MessageKey.Persona.SystemPersona,
            _ => null,
        };
        if (action is not null)
        {
            var word = MessageCatalog.Get(action, InterfaceLanguage.Zh);
            body.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, word) && !button.Classes.Contains("page-link")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            if (target != "onboarding")
            {
                Assert.Equal(title, window.FindControl<TextBlock>("SettingsHeading")!.Text);
                Assert.Contains(body.GetVisualDescendants().OfType<TextBox>(), box => box.IsEffectivelyVisible);
            }
        }

        return window.FindControl<Grid>(target == "onboarding" ? "SetupPanel" : "SettingsPanel")!;
    }

    /// <summary>Builds the same app with Skia drawing into a headless framebuffer.</summary>
    internal static class RasterBuilder
    {
        /// <summary>Creates the application builder consumed by the headless runner.</summary>
        /// <returns>The configured builder.</returns>
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false,
            });
        }
    }

    /// <summary>Replays the JSON exported by the React benchmark port, including file revisions.</summary>
    private sealed class ReferenceReads : HttpMessageHandler
    {
        /// <summary>Responses by endpoint.</summary>
        private readonly JsonObject _reads = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "page-inputs.json")))!.AsObject();

        /// <summary>Gets or sets the test's stateful endpoint handler.</summary>
        internal Func<HttpRequestMessage, Task<HttpResponseMessage?>>? Exchange
        {
            get; set;
        }

        /// <summary>Reads one captured endpoint for initial window state.</summary>
        /// <typeparam name="T">DTO supplied by the kernel.</typeparam>
        /// <param name="path">Captured endpoint.</param>
        /// <returns>The parsed DTO.</returns>
        public T Read<T>(string path) => _reads[path].Deserialize<T>(JsonSerializerOptions.Web)!;

        /// <inheritdoc/>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (Exchange is not null && await Exchange(request).ConfigureAwait(false) is { } response)
            {
                return response;
            }

            if (request.Method == HttpMethod.Post && path == "/themes")
            {
                var choice = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                var id = (string)choice!["id"]!;
                var packs = _reads[path]!.AsArray();
                Assert.True(id.Length == 0 || packs.Any(pack => (string?)pack?["id"] == id));
                foreach (var pack in packs)
                {
                    pack!["active"] = (string?)pack["id"] == id;
                }

                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            Assert.Equal(path == "/commit/propose" ? HttpMethod.Post : HttpMethod.Get, request.Method);
            if (_reads.ContainsKey(request.RequestUri.PathAndQuery))
            {
                path = request.RequestUri.PathAndQuery;
            }

            Assert.True(_reads.ContainsKey(path), path);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(_reads[path]?.ToJsonString() ?? "null", Encoding.UTF8, "application/json"),
            };
        }
    }
}
