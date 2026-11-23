using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Creates a provider from settings without changing the selected or default model.</summary>
    /// <param name="width">The reference viewport width.</param>
    /// <param name="dark">The reference color scheme.</param>
    /// <returns>The inline provider form interactions.</returns>
    [Theory]
    [InlineData(1440, false)]
    [InlineData(1440, true)]
    [InlineData(760, false)]
    [InlineData(760, true)]
    public Task ProviderSourceStaysInSettingsAndUsesTheKernelCatalogue(int width, bool dark) => WithSettings("conversation", (w, reads) =>
    {
        var height = width == 1440 ? 900 : 480;
        w.Width = width;
        w.Height = height;
        w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var inventory = reads.Read<JsonArray>("/providers");
        JsonNode? created = null;
        var modelWrites = 0;
        var probes = 0;
        var presetWrites = 0;
        reads.Exchange = async request =>
        {
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/providers/protocols":
                    return JsonReply(JsonNode.Parse("""[{"kind":"openai","answers":"chat","discovery":"openai","reasoningParams":true,"effortField":"reasoning_effort","effortUnder":["openai"]},{"kind":"responses","answers":"chat","discovery":"openai","reasoningParams":true,"effortField":"reasoning.effort"},{"kind":"typesafe","answers":"decision"}]""")!);
                case "/providers/presets":
                    return JsonReply(JsonNode.Parse("""
                        [
                            {"id":"deepseek-responses","label":"DeepSeek Official Responses API","vendor":"deepseek","kind":"responses","baseUrl":"https://api.deepseek.com"},
                            {"id":"glm-cn","label":"GLM CN API","vendor":"zhipu","kind":"openai","baseUrl":"https://open.bigmodel.cn/api/paas/v4"},
                            {"id":"zai-global","label":"Z.AI Global API","vendor":"zhipu","kind":"openai","baseUrl":"https://api.z.ai/api/paas/v4"},
                            {"id":"kimi-cn","label":"Kimi CN API","vendor":"kimi","kind":"openai","baseUrl":"https://api.moonshot.cn/v1"},
                            {"id":"kimi-global","label":"Kimi Global API","vendor":"kimi","kind":"openai","baseUrl":"https://api.moonshot.ai/v1"},
                            {"id":"minimax-cn-api","label":"MiniMax CN API","vendor":"minimax","kind":"openai","baseUrl":"https://api.minimaxi.com/v1"},
                            {"id":"qwen-cn","label":"Qwen CN API","vendor":"qwen","kind":"openai","baseUrl":"https://dashscope.aliyuncs.com/compatible-mode/v1"},
                            {"id":"mimo-api","label":"MiMo API","vendor":"mimo","kind":"openai","baseUrl":"https://api.xiaomimimo.com/v1"},
                            {"id":"longcat-openai","label":"LongCat OpenAI","vendor":"longcat","kind":"openai","baseUrl":"https://api.longcat.chat/openai/v1"},
                            {"id":"stepfun","label":"StepFun","vendor":"stepfun","kind":"openai","baseUrl":"https://api.stepfun.com/v1"},
                            {"id":"openrouter","label":"OpenRouter","vendor":"openrouter","kind":"openai","baseUrl":"https://openrouter.ai/api/v1"},
                            {"id":"openai","label":"OpenAI","vendor":"openai","kind":"openai","baseUrl":"https://api.openai.com/v1"},
                            {"id":"gemini","label":"Google Gemini","vendor":"google","kind":"openai","baseUrl":"https://generativelanguage.googleapis.com/v1beta/openai"},
                            {"id":"token-rhythm","label":"Token Rhythm","vendor":"token-rhythm","kind":"openai","baseUrl":"https://tokenrhythm.studio/v1"}
                        ]
                        """)!);
                case "/providers/presets/install":
                    var preset = JsonNode.Parse(await request.Content!.ReadAsStringAsync().ConfigureAwait(false))!;
                    Assert.Equal("deepseek-responses", (string?)preset["presetId"]);
                    Assert.Equal("fixture-preset", (string?)preset["name"]);
                    presetWrites++;
                    return JsonReply(JsonNode.Parse("""{"names":["fixture-preset"]}""")!);
                case "/providers/probe":
                    var probe = JsonNode.Parse(await request.Content!.ReadAsStringAsync().ConfigureAwait(false))!;
                    if ((string?)probe["baseUrl"] == "https://failed.fixture.invalid/v1")
                    {
                        return new HttpResponseMessage(HttpStatusCode.BadRequest)
                        {
                            Content = new StringContent("Fixture connection refused"),
                        };
                    }

                    Assert.Equal("https://models.fixture.invalid/v1", (string?)probe["baseUrl"]);
                    probes++;
                    return JsonReply(JsonNode.Parse("""{"kind":"openai","kinds":["openai","responses"],"authHeader":true,"ambiguous":true,"models":["deepseek-v4-pro","deepseek-flash"],"vision":["deepseek-v4-pro"]}""")!);
                case "/providers/check/model":
                    var model = JsonNode.Parse(await request.Content!.ReadAsStringAsync().ConfigureAwait(false))!;
                    return JsonReply(new JsonObject { ["status"] = "available", ["model"] = (string?)model["model"] });
                case "/providers" when request.Method == HttpMethod.Post:
                    created = JsonNode.Parse(await request.Content!.ReadAsStringAsync().ConfigureAwait(false))!;
                    var entry = created.DeepClone().AsObject();
                    entry.Remove("apiKey");
                    inventory.Add(entry);
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                case "/providers":
                    return JsonReply(inventory);
                case "/model" or "/default-model" when request.Method == HttpMethod.Post:
                    modelWrites++;
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                default:
                    return null;
            }
        };

        Navigate(w, "settings-model");
        ClickAt(w, "action:provider.add-start");
        Assert.True(w.FindControl<Grid>("SettingsPanel")!.IsEffectivelyVisible);
        Assert.False(w.FindControl<Grid>("SetupPanel")!.IsVisible);
        var scroller = w.FindControl<ScrollViewer>("SettingsScroller")!;
        var header = Named<Control>("SourceHeader");
        Align(Named<Border>("SourceFrame"));
        Assert.False(ControlAt<Button>(w, "action:provider.add").IsEnabled);
        Assert.NotEqual(default, ControlAt<TextBox>(w, "action:provider.draft.credential").PasswordChar);
        Capture("add");

        ClickAt(w, "action:provider.preset-toggle");
        Align(Named<Border>("PresetGallery"));
        Capture("add-presets");
        ClickAt(w, "action:provider.preset:deepseek-responses");
        Align(Named<Border>("SourceFrame"));
        Capture("add-preset");
        ClickAt(w, "action:provider.preset-back");
        ClickAt(w, "action:provider.advanced");
        Align(Named<Control>("AdvancedHeader"));
        Capture("add-advanced");
        Align(Named<TextBlock>("HeadersLabel"));
        Capture("add-advanced-headers");
        ClickAt(w, "action:provider.advanced");

        ControlAt<TextBox>(w, "action:provider.draft.endpoint").Text = "https://models.fixture.invalid/v1";
        ControlAt<TextBox>(w, "action:provider.draft.name").Text = "fixture-service";
        ControlAt<TextBox>(w, "action:provider.model-search").Text = "fixture-chat";
        Dispatcher.UIThread.RunJobs();
        Align(Named<Control>("CatalogueSection"));
        Capture("add-manual-offer");
        ClickAt(w, "action:provider.model-add");
        Align(Named<Control>("CatalogueSection"));
        Capture("add-manual");
        ClickAt(w, "action:provider.probe");
        PumpNavigation(() => probes == 1 && ControlAt<TextBlock>(w, "status:provider.probe").Text!.Contains("2", System.StringComparison.Ordinal));
        Assert.True(ControlAt<Button>(w, "action:provider.add").IsEnabled);
        ClickAt(w, "action:provider.add");
        PumpNavigation(() => created is not null && !w.GetVisualDescendants().Contains(header));
        Assert.Equal("fixture-service", (string?)created!["name"]);
        Assert.Equal("fixture-chat", (string?)created["default"]);
        Assert.Equal(new[] { "fixture-chat" }, created["models"]!.AsArray().Select(value => (string?)value));
        Assert.Equal(0, modelWrites);

        ClickAt(w, "action:provider.add-start");
        var endpoint = ControlAt<TextBox>(w, "action:provider.draft.endpoint");
        endpoint.Text = "https://models.fixture.invalid/v1";
        Dispatcher.UIThread.RunJobs();
        Align(Named<Control>("CatalogueSection"));
        ClickAt(w, "action:provider.probe");
        PumpNavigation(() => probes == 2 && ControlAt<TextBlock>(w, "status:provider.probe").Text!.Contains("2", System.StringComparison.Ordinal));
        Align(Named<Control>("CatalogueSection"));
        Capture("add-probe");
        endpoint.Text = "https://failed.fixture.invalid/v1";
        Dispatcher.UIThread.RunJobs();
        ClickAt(w, "action:provider.probe");
        PumpNavigation(() => ControlAt<TextBlock>(w, "status:provider.probe").Text!.Contains("Fixture connection refused", System.StringComparison.Ordinal));
        Align(Named<Control>("CatalogueSection"));
        Capture("add-probe-error");
        ClickAt(w, "action:provider.add-cancel");
        ClickAt(w, "action:provider.add-start");
        ClickAt(w, "action:provider.preset-toggle");
        ClickAt(w, "action:provider.preset:deepseek-responses");
        ControlAt<TextBox>(w, "action:provider.draft.preset-name").Text = "fixture-preset";
        Dispatcher.UIThread.RunJobs();
        ClickAt(w, "action:provider.add");
        PumpNavigation(() => presetWrites == 1 && !w.GetVisualDescendants().OfType<TextBox>().Any(box =>
            box.IsEffectivelyVisible && AutomationProperties.GetAutomationId(box) == "action:provider.draft.preset-name"));
        Assert.Equal(0, modelWrites);

        T Named<T>(string name)
            where T : Control => w.GetVisualDescendants().OfType<T>().Single(control => control.Name == name && control.IsEffectivelyVisible);

        void Align(Control control)
        {
            var y = control.TranslatePoint(default, w)!.Value.Y;
            scroller.Offset = new Vector(0, System.Math.Max(0, scroller.Offset.Y + y - 120));
            w.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        void Capture(string phase) => CaptureFlow(w, $"native-model-services-{phase}-{width}x{height}", resetScroll: false);
    });
}
