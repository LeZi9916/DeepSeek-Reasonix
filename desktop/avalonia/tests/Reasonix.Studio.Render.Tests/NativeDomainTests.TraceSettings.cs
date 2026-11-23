using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Saves one field at a time and displays when the change takes effect.</summary>
    /// <param name="width">The viewport width.</param><param name="dark">Whether to render the dark theme.</param><returns>The trace settings interaction.</returns>
    [Theory]
    [InlineData(1440, false)]
    [InlineData(1440, true)]
    [InlineData(760, false)]
    [InlineData(760, true)]
    public Task AccountSettingsTraceFormCommitsThroughItsHost(int width, bool dark) => WithSettings(
        "conversation",
        (w, reads) =>
        {
            var height = width == 1440 ? 900 : 480;
            w.Width = width;
            w.Height = height;
            w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var settings = JsonNode.Parse("""
                {"enabled":false,"endpoint":"","headerName":"","headerEnv":"","serviceName":"","headerEnvSet":false,"applies":"next_session","path":"/Users/you/.reasonix/config.toml"}
                """)!.AsObject();
            var changes = new List<JsonObject>();
            reads.Exchange = async request =>
            {
                if (request.RequestUri!.AbsolutePath != "/observability")
                {
                    return null;
                }

                if (request.Method == HttpMethod.Post)
                {
                    var patch = JsonNode.Parse(await request.Content!.ReadAsStringAsync().ConfigureAwait(false))!.AsObject();
                    foreach (var (key, value) in patch)
                    {
                        settings[key] = value?.DeepClone();
                    }

                    changes.Add(patch);
                }

                return JsonReply(settings);
            };

            Navigate(w, "settings-account");
            var endpoint = ControlAt<TextBox>(w, "action:trace-export.endpoint");
            var switcher = ControlAt<ToggleSwitch>(w, "action:trace-export.enabled");
            var block = w.GetVisualDescendants().OfType<Control>().Single(c => Equals(c.Tag, "data.trace_export"));
            var scroller = w.FindControl<ScrollViewer>("SettingsScroller")!;
            scroller.Offset = new Vector(0, block.TranslatePoint(default, scroller)!.Value.Y - 76);
            CaptureFlow(w, $"settings-account-trace-off-{width}x{height}", resetScroll: false);
            switcher.IsChecked = true;
            switcher.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True((bool?)Assert.Single(changes)["enabled"]);
            CaptureFlow(w, $"settings-account-trace-on-{width}x{height}", resetScroll: false);

            endpoint.Focus();
            endpoint.Text = "  http://localhost:4318  ";
            Assert.Single(changes);
            ControlAt<TextBox>(w, "action:trace-export.header-name").Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.Collection(
                changes,
                first => Assert.True((bool?)first["enabled"]),
                second => Assert.Equal("http://localhost:4318", (string?)second["endpoint"]));
            Assert.Equal("http://localhost:4318", endpoint.Text);
            switcher.Focus();
            CaptureFlow(w, $"settings-account-trace-configured-{width}x{height}", resetScroll: false);
        });
}
