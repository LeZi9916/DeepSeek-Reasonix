using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Shell.Settings;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Draft mode and controls follow the reference layout without switching the saved proxy.</summary>
    /// <param name="width">Viewport width.</param><param name="dark">Whether the viewport uses the dark theme.</param>
    /// <returns>The interaction and captured form states.</returns>
    [Theory]
    [InlineData(1440, false)]
    [InlineData(1440, true)]
    [InlineData(760, false)]
    [InlineData(760, true)]
    public Task NetworkSettingsKeepsDraftAndSavedDiagnosticsSeparate(int width, bool dark) => WithSettings("conversation", (w, reads) =>
    {
        var height = width == 1440 ? 900 : 480;
        w.Width = width;
        w.Height = height;
        w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var saved = new JsonObject
        {
            ["mode"] = "auto",
            ["effective"] = "auto (env)",
            ["endpoint"] = "https://api.deepseek.com/v1",
            ["direct"] = new JsonArray("api.mimo.cn"),
        };
        JsonObject? write = null;
        var diagnoses = 0;
        var refuse = true;
        reads.Exchange = async request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/network/diagnose")
            {
                Assert.Equal("auto", (string?)saved["mode"]);
                diagnoses++;
                return JsonReply(JsonNode.Parse("""
                    {"probes":[
                      {"step":"proxy","ok":true,"detail":"auto (env)","code":"net.proxy.via","params":{"proxy":"auto (env)"}},
                      {"step":"dns","ok":true,"detail":"proxy.corp → 10.0.0.9","durationMs":21},
                      {"step":"connect","ok":true,"detail":"通了","durationMs":180,"code":"net.connect.ok","params":{"host":"proxy.corp","port":"8080"}},
                      {"step":"tls","ok":true,"detail":"握手成功 · HTTP 200","durationMs":240,"code":"net.tls.ok","params":{"status":200}},
                      {"step":"auth","ok":false,"detail":"key 被拒了 — HTTP 401","durationMs":310,"advice":"换一个 key",
                       "code":"net.auth.rejected","adviceCode":"net.auth.rejected_advice","params":{"status":401}}
                    ]}
                    """)!);
            }

            if (path != "/network")
            {
                return null;
            }

            if (request.Method == HttpMethod.Post)
            {
                write = JsonNode.Parse(await request.Content!.ReadAsStringAsync().ConfigureAwait(false))!.AsObject();
                if (refuse)
                {
                    return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("proxy rejected") };
                }

                saved["mode"] = write["mode"]?.DeepClone();
                saved["server"] = write["server"]?.DeepClone();
                saved["port"] = write["port"]?.DeepClone();
                saved["effective"] = "http://proxy.corp:8080";
                saved["hasPassword"] = (bool?)write["clearPassword"] != true;
            }

            return JsonReply(saved);
        };

        Navigate(w, "settings-network");
        var proxy = w.GetVisualDescendants().OfType<NetworkSettings>().Single();
        var scroller = w.FindControl<ScrollViewer>("SettingsScroller")!;
        var save = ControlAt<Button>(w, "action:network.save");
        var effective = proxy.FindControl<TextBlock>("Effective")!;
        var fields = proxy.FindControl<StackPanel>("CustomFields")!;
        var results = proxy.FindControl<Border>("ProbeResults")!;
        Assert.False(save.IsEnabled);
        Assert.False(fields.IsVisible);
        Assert.Equal("auto (env)", effective.Text);
        Capture("auto");

        SelectMode("custom");
        Assert.True(ControlAt<RadioButton>(w, "action:network.mode.custom").IsChecked);
        Assert.True(fields.IsVisible);
        Assert.True(save.IsEnabled);
        Assert.Equal("auto (env)", effective.Text);
        Capture("custom");
        if (width == 760)
        {
            Capture("custom-fields");
        }

        ClickAt(w, "action:network.diagnose");
        Assert.Equal(1, diagnoses);
        Assert.Null(write);
        Assert.True(results.IsVisible);
        Assert.Equal(5, proxy.FindControl<ItemsControl>("Probes")!.ItemCount);
        Capture("diagnostics");

        SelectMode("off");
        Assert.False(fields.IsVisible);
        Assert.Equal("auto (env)", effective.Text);
        Assert.True(results.IsVisible);
        Capture("direct");
        SelectMode("custom");
        ControlAt<TextBox>(w, "field:network.server").Text = "proxy.corp";
        ControlAt<TextBox>(w, "field:network.port").Text = "80x80";
        ControlAt<TextBox>(w, "field:network.password").Text = "sample";
        Dispatcher.UIThread.RunJobs();
        ClickAt(w, "action:network.save");
        Assert.NotNull(write);
        Assert.Equal(8080, (int?)write["port"]);
        Assert.Equal("sample", (string?)write["password"]);
        Assert.True(proxy.FindControl<TextBlock>("Failure")!.IsVisible);
        Assert.True(results.IsVisible);
        Assert.True(save.IsEnabled);
        refuse = false;
        ClickAt(w, "action:network.save");
        Assert.Equal("http://proxy.corp:8080", effective.Text);
        Assert.False(save.IsEnabled);
        Assert.False(results.IsVisible);
        Assert.False(proxy.FindControl<TextBlock>("Failure")!.IsVisible);
        Assert.Equal(string.Empty, ControlAt<TextBox>(w, "field:network.password").Text);
        var clear = ControlAt<CheckBox>(w, "field:network.clear-password");
        clear.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        ClickAt(w, "action:network.save");
        Assert.True((bool?)write!["clearPassword"]);
        Assert.False(clear.IsEffectivelyVisible);
        Assert.False(save.IsEnabled);

        void SelectMode(string mode)
        {
            Assert.True(ControlAt<RadioButton>(w, "action:network.mode." + mode).Focus());
            w.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            w.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Dispatcher.UIThread.RunJobs();
        }

        void Capture(string state)
        {
            w.FindControl<ListBox>("SettingsNav")!.Focus();
            var scroll = state switch
            {
                "diagnostics" => width == 1440 ? 345 : 635,
                "custom-fields" => 365,
                _ => width == 760 ? 160 : 0,
            };
            scroller.Offset = new Vector(0, scroll);
            CaptureFlow(w, $"settings-network-{state}-{width}x{height}", resetScroll: false);
        }
    });
}
