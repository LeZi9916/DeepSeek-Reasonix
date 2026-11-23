using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Edits the six capability selections of one agent composition.</summary>
public sealed partial class PresetModules : SettingsFormView
{
    /// <summary>Supported composition module families.</summary>
    private static readonly (string Kind, string Label)[] s_kinds =
    [
        ("tools", "common.tool"), ("skills", "settings.skills"), ("mcp", "persona.mcp_servers"),
        ("extensions", "ext.extension"), ("hooks", "persona.automation_hooks"), ("commands", "persona.slash_commands"),
    ];

    /// <summary>Unparsed parameters are kept with the member the user is editing.</summary>
    private readonly Dictionary<JsonObject, string> _invalid = new();

    /// <summary>Initializes a new instance of the <see cref="PresetModules"/> class.</summary>
    public PresetModules() => InitializeComponent();

    /// <summary>Gets a value indicating whether each parameter field contains a JSON object.</summary>
    internal bool IsValid => _invalid.Count == 0;

    /// <summary>Summarizes modified module families for the discard prompt.</summary>
    /// <param name="changes">The change labels.</param>
    /// <param name="original">The initial definition.</param>
    /// <param name="draft">The current definition.</param>
    internal void AddChanges(ICollection<string> changes, JsonObject original, JsonObject draft)
    {
        foreach (var (kind, key) in s_kinds)
        {
            if (JsonNode.DeepEquals(original[kind], draft[kind]))
            {
                continue;
            }

            var count = (draft[kind]?["members"] as JsonArray)?.Count ?? 0;
            var value = count.ToString(CultureInfo.InvariantCulture);
            changes.Add(Text("persona.n_kind_items", ("kind", Text(key)), ("n", value)));
        }
    }

    /// <summary>Opens module editors over a detached draft.</summary>
    /// <param name="draft">The editable clone.</param>
    /// <param name="choices">Kernel-advertised module choices.</param>
    /// <param name="language">Shell language.</param>
    /// <param name="readOnly">Whether the opened definition is built in.</param>
    /// <param name="changed">Revalidates the parent form.</param>
    internal void Open(JsonObject draft, JsonObject? choices, InterfaceLanguage language, bool readOnly, Action changed)
    {
        Translate(language);
        _invalid.Clear();
        Tools.Tag = "tools";
        Extensions.Tag = "extensions";
        AutomationProperties.SetLabeledBy(Tools, ToolsLabel);
        AutomationProperties.SetLabeledBy(Extensions, ExtensionsLabel);
        Tools.IsChecked = Extensions.IsChecked = false;
        ToolGroups.Children.Clear();
        ExtensionGroups.Children.Clear();
        UpdateStatuses();
        foreach (var (kind, key) in s_kinds)
        {
            if (draft[kind] is not JsonObject config)
            {
                continue;
            }

            var body = new StackPanel { Spacing = 8, IsEnabled = !readOnly };
            var label = Text(key);
            body.Children.Add(new TextBlock { Text = label, FontWeight = Avalonia.Media.FontWeight.Medium });
            if (kind == "hooks")
            {
                body.Children.Add(Caption(Text("persona.safety_hooks_and_permission_rules")));
            }

            var modes = new[]
            {
                new Option("inherit", Text("persona.follow_current_configuration_including_resources")),
                new Option("select", Text("persona.use_selected_modules_only_empty")),
            };
            var controls = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,*"),
                ColumnSpacing = 8,
                RowDefinitions = new RowDefinitions("Auto,Auto"),
            };
            var mode = new ComboBox
            {
                ItemsSource = modes,
                SelectedItem = modes.FirstOrDefault(x => x.Id == (string?)config["mode"]),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = kind,
            };
            AutomationProperties.SetAutomationId(mode, "field:agent-preset.module-mode");
            AutomationProperties.SetName(mode, label + " " + Text("persona.selection_mode"));
            controls.Children.Add(mode);
            var picker = new PresetModulePicker();
            Grid.SetColumn(picker, 1);
            controls.Children.Add(picker);
            controls.SizeChanged += (_, e) =>
            {
                var narrow = e.NewSize.Width < 480;
                Grid.SetColumnSpan(mode, narrow || !picker.IsVisible ? 2 : 1);
                Grid.SetColumnSpan(picker, narrow ? 2 : 1);
                Grid.SetColumn(picker, narrow ? 0 : 1);
                Grid.SetRow(picker, narrow ? 1 : 0);
                controls.RowSpacing = narrow && picker.IsVisible ? 8 : 0;
            };
            body.Children.Add(controls);
            var entries = new StackPanel { Spacing = 8 };
            body.Children.Add(entries);
            var fieldset = new Border
            {
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 16),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Child = body,
            };
            fieldset.Bind(Border.BorderBrushProperty, fieldset.GetResourceObservable("BorderBrush"));
            (kind == "tools" ? ToolGroups : ExtensionGroups).Children.Add(fieldset);
            mode.SelectionChanged += (_, _) =>
            {
                if (mode.SelectedItem is Option next && next.Id != (string?)config["mode"])
                {
                    foreach (var member in config["members"]?.AsArray().OfType<JsonObject>() ?? [])
                    {
                        _invalid.Remove(member);
                    }

                    config["mode"] = next.Id;
                    config["members"] = new JsonArray();
                    RenderSelection();
                    UpdateStatuses();
                    changed();
                }
            };
            RenderSelection();

            void RenderSelection()
            {
                picker.IsVisible = (string?)config["mode"] == "select";
                Grid.SetColumnSpan(mode, !picker.IsVisible || controls.Bounds.Width < 480 ? 2 : 1);
                controls.RowSpacing = picker.IsVisible && controls.Bounds.Width < 480 ? 8 : 0;
                if (picker.IsVisible)
                {
                    var members = (config["members"] ??= new JsonArray()).AsArray();
                    picker.Open(kind, label, choices?[kind] as JsonArray, members, Language, member => _invalid.Remove(member), () =>
                    {
                        RenderMembers();
                        changed();
                    });
                }

                RenderMembers();
            }

            void RenderMembers()
            {
                entries.Children.Clear();
                if (!picker.IsVisible)
                {
                    return;
                }

                var members = config["members"]!.AsArray();
                foreach (var member in members.OfType<JsonObject>().ToArray())
                {
                    var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
                    var id = new TextBox { Text = (string?)member["id"], Tag = kind };
                    AutomationProperties.SetAutomationId(id, "field:agent-preset.module-id");
                    AutomationProperties.SetName(id, Text(key) + " ID");
                    id.PropertyChanged += (_, e) =>
                    {
                        if (e.Property == TextBox.TextProperty)
                        {
                            member["id"] = id.Text;
                            changed();
                        }
                    };
                    var remove = new Button { Content = Text("common.remove"), Tag = kind, Classes = { "preset-act" }, VerticalAlignment = VerticalAlignment.Center };
                    AutomationProperties.SetAutomationId(remove, "action:agent-preset.module-remove");
                    remove.Click += (_, _) =>
                    {
                        members.Remove(member);
                        _invalid.Remove(member);
                        picker.UpdateCount();
                        RenderMembers();
                        changed();
                    };
                    Grid.SetColumn(remove, 1);
                    line.Children.Add(id);
                    line.Children.Add(remove);
                    entries.Children.Add(line);
                    if (kind is not ("mcp" or "extensions"))
                    {
                        continue;
                    }

                    entries.Children.Add(Caption(Text("persona.per_composition_parameters_json_use")));
                    var parameters = new TextBox { Text = _invalid.GetValueOrDefault(member) ?? member["parameters"]?.ToJsonString(), AcceptsReturn = true, PlaceholderText = Text("persona.parameters"), MinHeight = 74, Tag = kind };
                    var warning = Caption(Text("persona.parameters_must_be_a_valid"));
                    warning.IsVisible = _invalid.ContainsKey(member);
                    AutomationProperties.SetAutomationId(parameters, "field:agent-preset.module-parameters");
                    AutomationProperties.SetName(parameters, Text("persona.parameters"));
                    parameters.PropertyChanged += (_, e) =>
                    {
                        if (e.Property != TextBox.TextProperty)
                        {
                            return;
                        }

                        var value = parameters.Text ?? string.Empty;
                        try
                        {
                            if (string.IsNullOrWhiteSpace(value))
                            {
                                member.Remove("parameters");
                            }
                            else
                            {
                                member["parameters"] = JsonNode.Parse(value) as JsonObject ?? throw new JsonException();
                            }

                            _invalid.Remove(member);
                        }
                        catch (JsonException)
                        {
                            _invalid[member] = value;
                        }

                        warning.IsVisible = _invalid.ContainsKey(member);
                        changed();
                    };
                    entries.Children.Add(parameters);
                    entries.Children.Add(warning);
                }

                var advancedBody = new StackPanel { Spacing = 6 };
                var manual = new Expander
                {
                    Header = Text("persona.advanced_enter_a_module_id"),
                    Content = advancedBody,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                advancedBody.Children.Add(Caption(Text("persona.use_only_for_modules_not")));
                var add = new Button { Content = Text("persona.add_blank_id"), Tag = kind, Classes = { "preset-act" } };
                AutomationProperties.SetAutomationId(add, "action:agent-preset.module-add");
                add.Click += (_, _) =>
                {
                    members.Add(new JsonObject { ["id"] = string.Empty });
                    picker.UpdateCount();
                    RenderMembers();
                    changed();
                };
                advancedBody.Children.Add(add);
                entries.Children.Add(manual);
            }
        }

        void UpdateStatuses()
        {
            ToolsStatus.Text = Text((string?)draft["tools"]?["mode"] == "select" ? "persona.selected_modules_only" : "persona.inherit_current_configuration");
            ExtensionsStatus.Text = Text(s_kinds.Skip(1).Any(kind => (string?)draft[kind.Kind]?["mode"] == "select")
                ? "common.custom"
                : "persona.inherit_current_configuration");
        }
    }
}
