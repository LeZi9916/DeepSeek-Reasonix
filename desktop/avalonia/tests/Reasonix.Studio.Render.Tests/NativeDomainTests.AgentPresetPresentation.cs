using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Reasonix.Studio.Shell.Settings;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace Reasonix.Studio.Render.Tests;

public sealed partial class NativeDomainTests
{
    /// <summary>Exercises the preset catalog and editor at both reference sizes.</summary>
    /// <param name="width">Viewport width.</param>
    /// <param name="height">Viewport height.</param>
    /// <param name="dark">Whether to use the dark theme.</param>
    /// <returns>The presentation and interaction check.</returns>
    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(1440, 900, true)]
    [InlineData(760, 480, false)]
    [InlineData(760, 480, true)]
    public Task AgentPresetPresentation(int width, int height, bool dark) => WithSettings("conversation", (w, reads) =>
    {
        w.Width = width;
        w.Height = height;
        w.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var options = reads.Read<JsonObject>("/agent-presets");
        var original = options["presets"]![0]!["definition"]!.DeepClone();
        original["id"] = "user:fixture-preset";
        original["name"] = "示例搭档";
        original["description"] = "用于检视自定义预设编辑器。";
        options["presets"]!.AsArray().Add(new JsonObject { ["definition"] = original, ["revision"] = "fixture-revision" });
        var imported = original.DeepClone();
        imported["id"] = "user:import-template";
        imported["name"] = "导入样例";
        reads.Exchange = request => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/agent-presets" => JsonReply(options),
            "/agent-presets/import" => JsonReply(new JsonObject { ["definition"] = imported.DeepClone(), ["diagnostics"] = new JsonArray() }),
            _ => null,
        });
        Navigate(w, "settings-session");
        var anchor = width >= 1000 ? 120 : 183;
        Assert.True(ControlAt<Button>(w, "action:settings.edit-persona").IsEffectivelyVisible);
        Assert.False(ControlAt<ToggleSwitch>(w, "action:settings.show-mode-picker").IsChecked);
        Capture("catalog");
        Capture("catalog-presets", Named("CatalogHeading"));
        var general = PresetAction("new-task-default", "builtin:general");
        var minimal = PresetAction("new-task-default", "builtin:minimal");
        Assert.True(general.Bounds.Height >= 115);
        var left = general.TranslatePoint(default, w)!.Value;
        var right = minimal.TranslatePoint(default, w)!.Value;
        if (width >= 1000)
        {
            Assert.Equal(left.Y, right.Y);
            Assert.InRange(right.X - left.X - general.Bounds.Width, 19, 23);
        }
        else
        {
            Assert.Equal(left.X, right.X);
            Assert.True(left.Y < right.Y);
        }

        var firstCard = general.GetVisualAncestors().OfType<Border>().First(border => border.Name == "BuiltinCard")!;
        Capture("catalog-cards", firstCard, anchor + 34);
        var secondCard = minimal.GetVisualAncestors().OfType<Border>().First(border => border.Name == "BuiltinCard")!;
        Capture("catalog-card-minimal", secondCard, anchor);
        Click(PresetAction("usage", "builtin:general"));
        Assert.Contains(firstCard.GetVisualDescendants().OfType<TextBlock>(), block => block.Name == "UsageCopy" && block.IsEffectivelyVisible);
        Capture("catalog-help", firstCard, anchor + 34);
        Click(PresetAction("usage", "builtin:general"));
        Capture("import-affordance", ControlAt<Button>(w, "action:agent-preset.import"), anchor);
        var custom = PresetAction("view", "user:fixture-preset");
        Capture("catalog-custom", custom, anchor + 20);
        Click(custom);
        Capture("custom", Named("EditorHeading"), anchor + 24);
        var extra = ControlAt<Expander>(w, "action:agent-preset.more");
        extra.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();
        Click(ControlAt<Button>(w, "action:agent-preset.delete-preview"));
        Capture("delete-preview", extra);
        Click(ControlAt<Button>(w, "action:agent-preset.back"));

        Click(PresetAction("view", "builtin:minimal"));
        Assert.True(ControlAt<TextBox>(w, "field:agent-preset.name").IsReadOnly);
        Capture("builtin", Named("EditorHeading"), anchor + 24);
        var tools = Target<ToggleButton>("action:agent-preset.capability", "tools");
        tools.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Capture("builtin-tools", tools, anchor);
        extra = ControlAt<Expander>(w, "action:agent-preset.more");
        extra.IsExpanded = true;
        Capture("builtin-actions", extra);
        Capture("builtin-footer", ControlAt<Button>(w, "action:agent-preset.leave"), anchor + (width >= 1000 ? 17 : 20));
        Click(ControlAt<Button>(w, "action:agent-preset.back"));
        Click(ControlAt<Button>(w, "action:agent-preset.create"));
        Assert.False(ControlAt<TextBox>(w, "field:agent-preset.name").IsReadOnly);
        Assert.False(ControlAt<Button>(w, "action:agent-preset.save").IsEnabled);
        Capture("new", Named("EditorHeading"), anchor + 24);
        var name = ControlAt<TextBox>(w, "field:agent-preset.name");
        name.Text = "示例搭档";
        Assert.True(ControlAt<Button>(w, "action:agent-preset.save").IsEnabled);
        Capture("new-ready", ControlAt<Button>(w, "action:agent-preset.save"), anchor + (width >= 1000 ? 17 : 20));
        tools = Target<ToggleButton>("action:agent-preset.capability", "tools");
        tools.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Target<ComboBox>("field:agent-preset.module-mode", "tools").SelectedIndex = 1;
        Scroll(tools, anchor);
        var trigger = Target<Button>("action:agent-preset.module-choose", "tools");
        Click(trigger);
        var picker = trigger.FindAncestorOfType<Reasonix.Studio.Shell.Settings.PresetModulePicker>()!;
        var search = picker.FindControl<TextBox>("Search")!;
        Assert.True(search.IsEffectivelyVisible);
        Capture("tool-picker");
        search.Text = "view_image";
        Dispatcher.UIThread.RunJobs();
        Capture("tool-picker-filter");
        var option = picker.FindControl<StackPanel>("Results")!.GetVisualDescendants().OfType<CheckBox>()
            .Single(box => Equals(box.Tag, "tools:view_image"));
        option.IsChecked = true;
        option.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.True(option.IsChecked);
        Capture("tool-picker-selected");
        Click(picker.FindControl<Button>("Clear")!);
        Assert.False(option.IsChecked);
        Capture("tool-picker-cleared");
        Click(picker.FindControl<Button>("Done")!);
        Assert.False(picker.FindControl<Avalonia.Controls.Primitives.Popup>("Picker")!.IsOpen);

        var extensions = Target<ToggleButton>("action:agent-preset.capability", "extensions");
        tools.IsChecked = false;
        extensions.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Target<ComboBox>("field:agent-preset.module-mode", "skills").SelectedIndex = 1;
        Scroll(extensions, anchor);
        var skills = Target<Button>("action:agent-preset.module-choose", "skills");
        Click(skills);
        var skillPicker = skills.FindAncestorOfType<Reasonix.Studio.Shell.Settings.PresetModulePicker>()!;
        Capture("skill-picker");
        Click(skillPicker.FindControl<Button>("Done")!);
        Click(ControlAt<Button>(w, "action:agent-preset.back"));
        Assert.True(w.FindControl<Border>("SettingsLeavePrompt")!.IsEffectivelyVisible);
        Capture("discard");
        Click(w.FindControl<Button>("DiscardSettings")!);
        Assert.False(name.IsEffectivelyVisible);

        var import = w.GetVisualDescendants().OfType<Reasonix.Studio.Shell.Settings.PresetImport>().Single();
        using var input = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(imported.ToJsonString()));
        var preview = import.PreviewAsync(input);
        Dispatcher.UIThread.RunJobs();
        Assert.True(preview.IsCompletedSuccessfully);
        Assert.Equal("导入样例 副本", ControlAt<TextBox>(w, "field:agent-preset.name").Text);
        Capture("import", Named("EditorHeading"), anchor + 24);
        Click(ControlAt<Button>(w, "action:agent-preset.back"));

        var modePicker = ControlAt<ToggleSwitch>(w, "action:settings.show-mode-picker");
        modePicker.IsChecked = true;
        Assert.True(w.FindControl<Button>("PlanMode")!.IsVisible);
        modePicker.IsChecked = false;
        Assert.False(w.FindControl<Button>("PlanMode")!.IsVisible);

        Click(ControlAt<Button>(w, "action:settings.edit-persona"));
        Capture("persona", Named("PersonaHeading"), anchor + 24);
        var savePersona = ControlAt<Button>(w, "action:persona.save");
        Assert.False(savePersona.IsEnabled);
        Capture("persona-footer", savePersona);
        Click(Target<Button>("action:persona.preset", "concise"));
        Assert.True(savePersona.IsEnabled);
        Capture("persona-preset", Named("PersonaFields"));
        Capture("persona-preset-footer", savePersona);
        Click(ControlAt<Button>(w, "action:persona.back"));
        Assert.True(w.FindControl<Border>("SettingsLeavePrompt")!.IsEffectivelyVisible);
        Click(w.FindControl<Button>("KeepSettings")!);
        Assert.True(savePersona.IsEffectivelyVisible);
        Assert.True(savePersona.IsEnabled);
        Click(ControlAt<Button>(w, "action:persona.back"));
        Click(w.FindControl<Button>("DiscardSettings")!);
        Assert.DoesNotContain(savePersona, w.GetVisualDescendants());

        void Capture(string phase, Control? element = null, double? y = null)
        {
            if (element is not null)
            {
                Scroll(element, y ?? anchor);
            }

            CaptureFlow(w, $"native-agent-presets-{phase}-{width}x{height}", resetScroll: false);
        }

        void Scroll(Control element, double y)
        {
            Dispatcher.UIThread.RunJobs();
            var scroll = w.FindControl<ScrollViewer>("SettingsScroller")!;
            var offset = element.TranslatePoint(default, w)!.Value.Y - y;
            scroll.Offset = new Vector(0, scroll.Offset.Y + offset);
            Dispatcher.UIThread.RunJobs();
        }

        Control Named(string name) => w.GetVisualDescendants().OfType<Control>().Single(c => c.Name == name && c.IsEffectivelyVisible);

        Button PresetAction(string action, string id) => Target<Button>("action:agent-preset." + action, id);

        T Target<T>(string action, string id)
            where T : Control => w.GetVisualDescendants().OfType<T>().Single(control => control.IsEffectivelyVisible &&
                AutomationProperties.GetAutomationId(control) == action && Equals(control.Tag, id));

        static void Click(Button button)
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
        }
    });

    /// <summary>Checks the preset catalog's reference vertical rhythm.</summary>
    /// <returns>The layout check.</returns>
    [Fact]
    public Task AgentPresetCatalogUsesTheReferenceVerticalRhythm() => WithSettings("settings-session", (w, _) =>
    {
        var presets = w.GetVisualDescendants().OfType<PresetWorkspace>().Single();
        var create = presets.FindControl<Button>("Create")!;
        var mode = presets.FindControl<ToggleSwitch>("ModePicker")!;
        var card = w.GetVisualDescendants().OfType<PresetCatalogEntry>().First();
        double Y(Control control) => control.TranslatePoint(default, w)!.Value.Y;
        Assert.InRange(Y(create), 337.4, 338.4);
        Assert.InRange(Y(mode), 449.8, 450.8);
        Assert.InRange(Y(card), 617.0, 618.0);
    });
}
