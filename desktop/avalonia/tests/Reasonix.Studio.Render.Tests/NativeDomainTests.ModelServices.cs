using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Shell.Settings;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Exercises the provider rail and its protocol sheet against the captured inventory.</summary>
    /// <param name="width">Window width.</param>
    /// <param name="dark">Whether dark mode is selected.</param>
    /// <returns>The model settings interactions.</returns>
    [Theory]
    [InlineData(1440, false)]
    [InlineData(1440, true)]
    [InlineData(760, false)]
    [InlineData(760, true)]
    public Task ModelServicesUsesTheReferenceRailAndProtocolSheet(int width, bool dark) => WithSettings("conversation", (w, reads) =>
    {
        var height = width == 1440 ? 900 : 480;
        w.Width = width;
        w.Height = height;
        w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        w.UpdateLayout();
        string? probed = null;
        JsonNode? renameWrite = null;
        JsonNode? editWrite = null;
        var inventory = reads.Read<JsonArray>("/providers");
        reads.Exchange = async request =>
        {
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/providers":
                    return JsonReply(inventory);
                case "/providers/check":
                    var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync().ConfigureAwait(false));
                    probed = (string?)body?["name"];
                    return JsonReply(new JsonObject
                    {
                        ["ok"] = false,
                        ["code"] = "provider.probe.unauthorized",
                        ["httpStatus"] = 401,
                        ["detail"] = "Invalid Authentication",
                    });
                case "/providers/display-name":
                    renameWrite = JsonNode.Parse(await request.Content!.ReadAsStringAsync().ConfigureAwait(false));
                    foreach (var value in renameWrite!["names"]!.AsArray())
                    {
                        var provider = inventory.First(item => (string?)item?["name"] == (string?)value)!;
                        provider["displayName"] = (string?)renameWrite["displayName"];
                    }

                    return JsonReply(new JsonObject { ["ok"] = true });
                case "/providers/edit":
                    editWrite = JsonNode.Parse(await request.Content!.ReadAsStringAsync().ConfigureAwait(false));
                    var saved = inventory.First(item => (string?)item?["name"] == (string?)editWrite?["name"])!;
                    saved["contextWindow"] = editWrite?["contextWindow"]?.DeepClone();
                    return JsonReply(new JsonObject { ["ok"] = true });
                default:
                    return null;
            }
        };

        Navigate(w, "settings-model");
        var panel = Assert.Single(w.GetVisualDescendants().OfType<ModelServices>());
        var rail = panel.FindControl<StackPanel>("ProviderRail")!;
        var sheet = panel.FindControl<ContentControl>("ProviderDetail")!;
        var scroller = w.FindControl<ScrollViewer>("SettingsScroller")!;
        var rows = rail.GetVisualDescendants().OfType<Button>()
            .Where(button => AutomationProperties.GetAutomationId(button) == "action:provider.select").ToArray();
        Assert.Equal(3, rows.Length);
        Assert.Equal(new[] { "deepseek-anthropic", "myrelay", "myrelay-work" }, rows.Select(AutomationProperties.GetName));
        Assert.All(rows.Take(2), row => Assert.Contains(row.GetVisualDescendants().OfType<ContentControl>(), mark => mark.Name == "VendorMark"));
        Assert.DoesNotContain(rows[2].GetVisualDescendants().OfType<ContentControl>(), mark => mark.Name == "VendorMark");
        Assert.Contains(rail.GetVisualDescendants().OfType<TextBlock>(), item => item.Text == "api.deepseek.com");
        var add = Action(rail, "provider.add-start");
        Assert.Equal(32, add.Bounds.Height);
        Assert.Equal(rail.Bounds.Width, add.Bounds.Width);
        var railPosition = rail.TranslatePoint(default, panel)!.Value;
        var sheetPosition = sheet.TranslatePoint(default, panel)!.Value;
        if (width == 1440)
        {
            Assert.Equal(224, rail.Bounds.Width);
            Assert.Equal(244, sheetPosition.X - railPosition.X);
        }
        else
        {
            Assert.Equal(railPosition.X, sheetPosition.X);
            Assert.Equal(rail.Bounds.Width, sheet.Bounds.Width);
            Assert.True(sheetPosition.Y >= railPosition.Y + rail.Bounds.Height);
        }

        Capture("normal");
        Align(rail);
        Capture("rail");
        var second = rows[1];
        var underPointer = second.TranslatePoint(new Point(32, 15), w)!.Value;
        w.MouseMove(underPointer);
        Dispatcher.UIThread.RunJobs();
        Assert.True(second.IsPointerOver);
        Capture("hover");
        Click(second);
        Assert.Contains(sheet.GetVisualDescendants().OfType<TextBlock>(), item => item.Name == "ProviderTitle" && item.Text == "myrelay");
        Capture("selected");
        Click(rows[0]);
        Align(sheet);
        Capture("detail");
        var protocol = sheet.GetVisualDescendants().OfType<Button>().Single(button =>
            AutomationProperties.GetAutomationId(button) == "action:provider.protocol" && Equals(button.Tag, "anthropic"));
        Click(protocol);
        Assert.Contains(sheet.GetVisualDescendants().OfType<TextBlock>(), item => item.Name == "ProviderSubtitle" && item.Text == "Anthropic 兼容 · api.deepseek.com");
        var address = Assert.Single(sheet.GetVisualDescendants().OfType<TextBox>(), item => item.Name == "Endpoint");
        Assert.Equal("https://api.deepseek.com/anthropic", address.Text);
        Capture("protocol");
        Click(Protocol("openai"));
        Click(Action(sheet, "provider.probe"));
        PumpNavigation(() => sheet.GetVisualDescendants().OfType<TextBlock>().Any(item =>
            item.Name == "CheckBody" && item.IsEffectivelyVisible && item.Text!.Contains("HTTP 401", System.StringComparison.Ordinal)));
        Assert.Equal("deepseek", probed);
        Capture("probe");

        Align(rail);
        Click(Action(rail, "provider.rename-start"));
        var rename = rail.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "RenameInput" && box.IsEffectivelyVisible);
        Assert.Equal("deepseek-anthropic", rename.Text);
        Capture("rename");
        KeyPress(Key.Escape, PhysicalKey.Escape);
        Assert.Null(renameWrite);
        Click(Action(rail, "provider.rename-start"));
        rename.Text = "团队服务";
        KeyPress(Key.Enter, PhysicalKey.Enter);
        PumpNavigation(() => sheet.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Name == "ProviderTitle" && text.Text == "团队服务"));
        Assert.Equal(new[] { "deepseek", "deepseek-anthropic" }, renameWrite!["names"]!.AsArray().Select(item => (string?)item));
        Assert.Equal("团队服务", (string?)renameWrite["displayName"]);

        var key = sheet.GetVisualDescendants().OfType<TextBox>().Single(control => control.Name == "ApiKey");
        var apply = Action(sheet, "provider.save");
        Assert.False(apply.IsEnabled);
        Assert.NotEqual(default, key.PasswordChar);
        key.Text = "mock-secret-value";
        PumpNavigation(() => apply.IsEnabled);
        var endpoint = sheet.GetVisualDescendants().OfType<TextBox>().Single(control => control.Name == "Endpoint");
        Align(endpoint, 145);
        Capture("draft");
        Click(Action(sheet, "provider.revert"));
        Assert.True(string.IsNullOrEmpty(key.Text));
        Assert.False(apply.IsEnabled);
        Assert.Null(editWrite);
        var context = sheet.GetVisualDescendants().OfType<TextBox>().Single(field => field.Name == "ContextLimit");
        context.Text = "128000";
        PumpNavigation(() => apply.IsEnabled);
        Click(apply);
        PumpNavigation(() => sheet.GetVisualDescendants().OfType<TextBox>().Any(field => field.Name == "ContextLimit" && field.Text == "128000" && !ReferenceEquals(field, context)) && editWrite is not null);
        Assert.Equal("deepseek", (string?)editWrite!["name"]);
        Assert.Equal(128000, (int?)editWrite["contextWindow"]);

        Button Protocol(string kind) => sheet.GetVisualDescendants().OfType<Button>().Single(button =>
            AutomationProperties.GetAutomationId(button) == "action:provider.protocol" && Equals(button.Tag, kind));

        void KeyPress(Key keyCode, PhysicalKey physical)
        {
            w.KeyPress(keyCode, RawInputModifiers.None, physical, string.Empty);
            w.KeyRelease(keyCode, RawInputModifiers.None, physical, string.Empty);
            Dispatcher.UIThread.RunJobs();
        }

        static Button Action(Control scope, string id) => scope.GetVisualDescendants().OfType<Button>()
            .First(button => AutomationProperties.GetAutomationId(button) == "action:" + id && button.IsEffectivelyVisible);

        static void Click(Button button)
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }

        void Align(Control control, double top = 120)
        {
            var y = control.TranslatePoint(default, w)!.Value.Y;
            scroller.Offset = new Vector(0, scroller.Offset.Y + y - top);
            w.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        void Capture(string state) => CaptureFlow(w, $"native-model-services-{state}-{width}x{height}", resetScroll: false);
    });
}
