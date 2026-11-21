using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
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
        Groups.Children.Clear();
        foreach (var (kind, key) in s_kinds)
        {
            if (draft[kind] is not JsonObject config)
            {
                continue;
            }

            var body = new StackPanel { Spacing = 5, IsEnabled = !readOnly };
            var modes = new[] { new Option("inherit", Text("persona.follow_current_configuration_including_resources")), new Option("select", Text("persona.use_selected_modules_only_empty")) };
            var mode = new ComboBox { ItemsSource = modes, SelectedItem = modes.FirstOrDefault(x => x.Id == (string?)config["mode"]), HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetAutomationId(mode, "field:agent-preset.module-mode");
            AutomationProperties.SetName(mode, Text(key));
            body.Children.Add(mode);
            var entries = new StackPanel { Spacing = 6 };
            body.Children.Add(entries);
            Groups.Children.Add(new Expander { Header = Text(key), Content = body, HorizontalAlignment = HorizontalAlignment.Stretch });
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
                    RenderMembers();
                    changed();
                }
            };
            RenderMembers();

            void RenderMembers()
            {
                entries.Children.Clear();
                if ((string?)config["mode"] != "select")
                {
                    return;
                }

                var members = (config["members"] ??= new JsonArray()).AsArray();
                foreach (var member in members.OfType<JsonObject>().ToArray())
                {
                    var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
                    var id = new TextBox { Text = (string?)member["id"] };
                    AutomationProperties.SetName(id, Text(key) + " ID");
                    id.PropertyChanged += (_, e) =>
                    {
                        if (e.Property == TextBox.TextProperty)
                        {
                            member["id"] = id.Text;
                            changed();
                        }
                    };
                    var remove = new Button { Content = Text("common.remove") };
                    AutomationProperties.SetAutomationId(remove, "action:agent-preset.module-remove");
                    remove.Click += (_, _) =>
                    {
                        members.Remove(member);
                        _invalid.Remove(member);
                        RenderMembers();
                        changed();
                    };
                    Grid.SetColumn(remove, 1);
                    line.Children.Add(id);
                    line.Children.Add(remove);
                    entries.Children.Add(line);
                    var parameters = new TextBox { Text = _invalid.GetValueOrDefault(member) ?? member["parameters"]?.ToJsonString(), AcceptsReturn = true, PlaceholderText = Text("persona.parameters") };
                    var warning = Caption(Text("persona.parameters_must_be_a_valid"));
                    warning.IsVisible = _invalid.ContainsKey(member);
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

                var available = (choices?[kind]?.AsArray().OfType<JsonObject>() ?? [])
                    .Select(o => (string?)o["id"]).OfType<string>().Where(id => !members.Any(m => (string?)m?["id"] == id)).ToArray();
                var pick = new ComboBox { ItemsSource = available, PlaceholderText = Text("common.pick_one"), HorizontalAlignment = HorizontalAlignment.Stretch };
                AutomationProperties.SetName(pick, Text(key));
                entries.Children.Add(pick);
                var add = new Button { Content = Text("persona.add_blank_id") };
                AutomationProperties.SetAutomationId(add, "action:agent-preset.module-add");
                pick.SelectionChanged += (_, _) =>
                {
                    if (pick.SelectedItem is string chosen)
                    {
                        Add(chosen);
                    }
                };
                add.Click += (_, _) => Add(string.Empty);
                entries.Children.Add(add);

                void Add(string id)
                {
                    members.Add(new JsonObject { ["id"] = id });
                    RenderMembers();
                    changed();
                }
            }
        }
    }
}
