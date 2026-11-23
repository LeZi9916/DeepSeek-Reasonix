using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>A searchable checklist for kernel-advertised modules in one preset family.</summary>
public sealed partial class PresetModulePicker : SettingsFormView
{
    /// <summary>Choice controls in keyboard navigation order.</summary>
    private readonly List<(Choice Item, CheckBox Box)> _boxes = new();

    /// <summary>Group heading controls updated when selection changes.</summary>
    private readonly List<(Choice[] Items, TextBlock Count, Button Select)> _groups = new();

    /// <summary>Options in the order advertised by the kernel.</summary>
    private Choice[] _choices = [];

    /// <summary>Options matched by the current search.</summary>
    private Choice[] _visible = [];

    /// <summary>The current draft selection.</summary>
    private JsonArray _members = new();

    /// <summary>The kernel's name for this family.</summary>
    private string _kind = string.Empty;

    /// <summary>The callback that clears validation for a removed member.</summary>
    private Action<JsonObject>? _removed;

    /// <summary>The callback that revalidates the preset.</summary>
    private Action? _changed;

    /// <summary>Initializes a new instance of the <see cref="PresetModulePicker"/> class.</summary>
    public PresetModulePicker()
    {
        InitializeComponent();
        Trigger.Click += (_, _) => TogglePopup();
        Search.TextChanged += (_, _) =>
        {
            RenderChoices();
            PlacePopup();
        };
        All.Click += (_, _) => SetSelected(_visible, !_visible.All(item => IsSelected(item.Id)));
        Clear.Click += (_, _) =>
        {
            foreach (var member in _members.OfType<JsonObject>())
            {
                _removed?.Invoke(member);
            }

            _members.Clear();
            Commit();
        };
        Done.Click += (_, _) => Close();
        Picker.Opened += (_, _) => Search.Focus();
        PickerSurface.AddHandler(KeyDownEvent, OnPickerKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>Attaches the picker to the current members instead of copying their parameter objects.</summary>
    /// <param name="kind">The family ID.</param>
    /// <param name="label">The localized family label.</param>
    /// <param name="choices">The source options.</param>
    /// <param name="members">The live draft members.</param>
    /// <param name="language">The current UI language.</param>
    /// <param name="removed">Clears validation for a removed member.</param>
    /// <param name="changed">Refreshes the editor after a change.</param>
    internal void Open(string kind, string label, JsonArray? choices, JsonArray members, InterfaceLanguage language, Action<JsonObject> removed, Action changed)
    {
        Translate(language);
        Trigger.Tag = Search.Tag = All.Tag = Clear.Tag = Done.Tag = kind;
        Search.PlaceholderText = Text("persona.search_module_names_or_descriptions");
        Clear.Content = Text("common.clear");
        Done.Content = Text("common.done");
        AutomationProperties.SetName(Trigger, label + " " + Text("persona.add_module"));
        AutomationProperties.SetName(Search, Text("persona.search_modules"));
        _kind = kind;
        _members = members;
        _removed = removed;
        _changed = changed;
        _choices = (choices?.OfType<JsonObject>() ?? [])
            .Where(choice => choice["id"] is not null)
            .Select(choice => new Choice((string)choice["id"]!, (string?)choice["description"] ?? string.Empty, (string?)choice["source"] ?? string.Empty))
            .DistinctBy(choice => choice.Id)
            .ToArray();
        UpdateCount();
    }

    /// <summary>Refreshes the count when the form adds or removes a manual ID.</summary>
    internal void UpdateCount()
    {
        var count = Text("persona.n_selected", ("n", Count(_members.Count)));
        TriggerText.Text = Text("persona.add_module") + " · " + count;
        Total.Text = count;
    }

    /// <summary>Formats one count using the current locale.</summary>
    /// <param name="count">Number of entries.</param>
    /// <returns>Text used in a localized string.</returns>
    private static string Count(int count) => count.ToString(CultureInfo.InvariantCulture);

    /// <summary>Shows or dismisses the popup at the chooser button.</summary>
    private void TogglePopup()
    {
        if (Picker.IsOpen)
        {
            Close();
            return;
        }

        Search.Text = string.Empty;
        RenderChoices();
        PlacePopup();
        Picker.IsOpen = true;
    }

    /// <summary>Positions the panel near its trigger with the reference viewport gutter.</summary>
    private void PlacePopup()
    {
        if (TopLevel.GetTopLevel(this) is not { } top || Trigger.TranslatePoint(default, top) is not { } origin)
        {
            return;
        }

        var width = Math.Min(480, Math.Max(0, top.ClientSize.Width - 24));
        var heightLimit = Math.Clamp(top.ClientSize.Height - 222, 40, 360);
        var results = _visible.Length == 0 ? 52 : (_visible.Length * 40) + (_groups.Count * 34) + (Math.Max(0, _groups.Count - 1) * 13) + 8;
        var height = 198 + Math.Min(heightLimit, results);
        var below = origin.Y + Trigger.Bounds.Height + 6;
        var above = origin.Y - height - 6;
        var preferred = below + height > top.ClientSize.Height - 12 && above >= 12 ? above : below;
        PickerSurface.Width = width;
        ResultsViewport.MaxHeight = heightLimit;
        Picker.HorizontalOffset = Math.Clamp(origin.X, 12, Math.Max(12, top.ClientSize.Width - width - 12)) - origin.X;
        Picker.VerticalOffset = Math.Clamp(preferred, 12, Math.Max(12, top.ClientSize.Height - height - 12)) - origin.Y - Trigger.Bounds.Height;
    }

    /// <summary>Closes the popup and returns focus to the chooser.</summary>
    private void Close()
    {
        Picker.IsOpen = false;
        Trigger.Focus();
    }

    /// <summary>Builds the result groups for the current query.</summary>
    private void RenderChoices()
    {
        Results.Children.Clear();
        _boxes.Clear();
        _groups.Clear();
        var needle = Search.Text?.Trim() ?? string.Empty;
        _visible = _choices.Where(choice => (choice.Id + " " + choice.Description).Contains(needle, StringComparison.OrdinalIgnoreCase)).ToArray();
        All.Content = Text(needle.Length == 0 ? "persona.select_all_n" : "persona.select_all_results_n", ("n", Count(_visible.Length)));
        foreach (var (source, key) in new[] { ("builtin", "common.built_in"), ("custom", "common.custom"), ("unknown", "persona.unclassified") })
        {
            var items = _visible.Where(choice => (choice.Source is "builtin" or "custom" ? choice.Source : "unknown") == source).ToArray();
            if (items.Length == 0)
            {
                continue;
            }

            var header = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                ColumnSpacing = 10,
                MinHeight = 34,
                Margin = new Thickness(14, 0),
            };
            header.Children.Add(new TextBlock { Text = Text(key), FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center });
            var count = Caption(string.Empty);
            count.VerticalAlignment = VerticalAlignment.Center;
            count.Bind(TextBlock.ForegroundProperty, count.GetResourceObservable("MutedBrush"));
            Grid.SetColumn(count, 1);
            header.Children.Add(count);
            var select = new Button
            {
                Tag = _kind + ":" + source,
                VerticalAlignment = VerticalAlignment.Center,
                Classes = { "preset-flat", "preset-muted", "module-action" },
            };
            AutomationProperties.SetAutomationId(select, "action:agent-preset.module-select-group");
            select.Click += (_, _) => SetSelected(items, !items.All(item => IsSelected(item.Id)));
            Grid.SetColumn(select, 2);
            header.Children.Add(select);
            if (_groups.Count > 0)
            {
                Results.Children.Add(new Border
                {
                    Height = 1,
                    Margin = new Thickness(0, 6),
                    [!Border.BackgroundProperty] = StudioTypography.Resource("HairBrush"),
                });
            }

            Results.Children.Add(header);
            _groups.Add((items, count, select));
            foreach (var item in items)
            {
                AddChoice(item);
            }
        }

        if (_visible.Length == 0)
        {
            var empty = Caption(Text(_choices.Length == 0 ? "persona.no_modules_available" : "persona.no_matching_modules"));
            empty.Margin = new Thickness(16);
            Results.Children.Add(empty);
        }

        ResultsViewport.Offset = default;
        RefreshSelection();
    }

    /// <summary>Creates a selectable row from a kernel option.</summary>
    /// <param name="item">The option rendered on this row.</param>
    private void AddChoice(Choice item)
    {
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("16,Auto,*"), ColumnSpacing = 10 };
        var icon = new PathIcon
        {
            Width = 16,
            Height = 16,
            Data = Geometry.Parse(item.Source == "builtin"
                ? "M7 6L1 12l6 6 1-1-5-5 5-5Z M17 6l6 6-6 6-1-1 5-5-5-5Z"
                : "M12 2 1 7l11 5 11-5Z M1 12l11 5 11-5v2l-11 5-11-5Z M1 17l11 5 11-5v2l-11 5-11-5Z"),
            [!PathIcon.ForegroundProperty] = StudioTypography.Resource("MutedBrush"),
        };
        content.Children.Add(icon);
        var id = new TextBlock { Text = item.Id, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(id, 1);
        content.Children.Add(id);
        var description = Caption(item.Description);
        description.VerticalAlignment = VerticalAlignment.Center;
        description.TextWrapping = TextWrapping.NoWrap;
        description.TextTrimming = TextTrimming.CharacterEllipsis;
        description.Bind(TextBlock.ForegroundProperty, description.GetResourceObservable("MutedBrush"));
        Grid.SetColumn(description, 2);
        content.Children.Add(description);
        ToolTip.SetTip(content, item.Description);
        var box = new CheckBox { Content = content, Tag = _kind + ":" + item.Id, Classes = { "module-choice" } };
        AutomationProperties.SetAutomationId(box, "action:agent-preset.module-toggle");
        AutomationProperties.SetName(box, item.Id);
        box.Click += (_, _) => SetSelected([item], box.IsChecked == true);
        _boxes.Add((item, box));
        Results.Children.Add(box);
    }

    /// <summary>Checks whether an ID is already part of this draft.</summary>
    /// <param name="id">The option's ID.</param>
    /// <returns>Whether the option is selected.</returns>
    private bool IsSelected(string id) => _members.Any(member => (string?)member?["id"] == id);

    /// <summary>Adds or removes a subset of options while retaining other member objects.</summary>
    /// <param name="items">The options affected.</param>
    /// <param name="select">Whether to add them.</param>
    private void SetSelected(Choice[] items, bool select)
    {
        if (select)
        {
            foreach (var item in items.Where(item => !IsSelected(item.Id)))
            {
                _members.Add(new JsonObject { ["id"] = item.Id });
            }
        }
        else
        {
            var ids = items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var member in _members.OfType<JsonObject>().Where(member => (string?)member["id"] is string id && ids.Contains(id)).ToArray())
            {
                _removed?.Invoke(member);
                _members.Remove(member);
            }
        }

        Commit();
    }

    /// <summary>Updates the summary and parent form after editing membership.</summary>
    private void Commit()
    {
        RefreshSelection();
        _changed?.Invoke();
    }

    /// <summary>Refreshes group counters and checkboxes without replacing the focused element.</summary>
    private void RefreshSelection()
    {
        UpdateCount();
        foreach (var (item, box) in _boxes)
        {
            box.IsChecked = IsSelected(item.Id);
        }

        var selected = _visible.Count(item => IsSelected(item.Id));
        All.IsChecked = selected == 0 ? false : selected == _visible.Length ? true : null;
        All.IsEnabled = _visible.Length > 0;
        Clear.IsEnabled = _members.Count > 0;
        foreach (var (items, count, select) in _groups)
        {
            var chosen = items.Count(item => IsSelected(item.Id));
            count.Text = Text("persona.selected_total_selected", ("selected", Count(chosen)), ("total", Count(items.Length)));
            select.Content = Text(chosen == items.Length ? "persona.deselect_group" : "persona.select_group");
        }
    }

    /// <summary>Moves focus between options or closes the popup without leaving the editor.</summary>
    /// <param name="sender">The popup surface.</param>
    /// <param name="e">The keystroke.</param>
    private void OnPickerKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
        else if ((e.Key is Key.Up or Key.Down) && _boxes.Count > 0)
        {
            e.Handled = true;
            var index = _boxes.FindIndex(row => row.Box.IsFocused);
            var next = e.Key == Key.Down
                ? (index + 1) % _boxes.Count
                : (index <= 0 ? _boxes.Count : index) - 1;
            _boxes[next].Box.Focus();
            _boxes[next].Box.BringIntoView();
        }
    }

    /// <summary>One advertised module and its searchable metadata.</summary>
    /// <param name="Id">Canonical module ID.</param>
    /// <param name="Description">Kernel-provided description.</param>
    /// <param name="Source">Origin shown in the group heading.</param>
    private sealed record Choice(string Id, string Description, string Source);
}
