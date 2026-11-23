using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>A model's purpose and the controls that choose its source and effort.</summary>
internal sealed partial class ModelUseRow : UserControl
{
    /// <summary>Whether the connection simply follows the default model.</summary>
    private bool _follows;

    /// <summary>Initializes a new instance of the <see cref="ModelUseRow"/> class.</summary>
    public ModelUseRow() => InitializeComponent();

    /// <summary>Gets the panel for any per-profile overrides.</summary>
    internal WrapPanel OverridePanel => Overrides;

    /// <summary>Displays a purpose and the currently resolved source.</summary>
    /// <param name="purpose">Role identifier, including the default model.</param>
    /// <param name="title">Localized purpose.</param>
    /// <param name="hint">Localized summary.</param>
    /// <param name="connection">Effective connection.</param>
    /// <param name="traits">Details of the effective connection.</param>
    /// <param name="follows">Whether to hide the inherited connection at small widths.</param>
    /// <param name="language">Interface language.</param>
    internal void Present(string purpose, string title, string hint, string connection, string traits, bool follows, InterfaceLanguage language)
    {
        Tag = purpose;
        _follows = follows;
        RoleTitle.Text = title;
        RoleHint.Text = hint;
        Connection.Text = connection;
        Connection.IsVisible = connection.Length > 0;
        ConnectionHint.Text = traits;
        ConnectionHint.IsVisible = traits.Length > 0;
        EffortCaption.Text = MessageCatalog.Get("usage.effort", language);
        AutomationProperties.SetName(ModelPicker, title);
        var effortName = purpose == "default"
            ? MessageCatalog.Get("usage.default_model_reasoning_effort", language)
            : MessageCatalog.Get("usage.name_reasoning_effort", language, ("name", title));
        AutomationProperties.SetName(EffortPicker, effortName);
        ModelPicker.Tag = EffortPicker.Tag = purpose;
        AutomationProperties.SetAutomationId(ModelPicker, purpose == "default" ? "action:model.select" : "action:roles.model");
        AutomationProperties.SetAutomationId(EffortPicker, purpose == "default" ? "action:reasoning.effort" : "action:roles.effort");
    }

    /// <summary>Arranges the row as four columns or a single column at the Electron breakpoint.</summary>
    /// <param name="compact">Whether controls stack beneath their labels.</param>
    internal void SetCompact(bool compact)
    {
        UseFrame.MinHeight = compact ? 0 : 58;
        Cells.ColumnDefinitions[1].Width = compact ? new GridLength(0) : new GridLength(1.6, GridUnitType.Star);
        Cells.ColumnDefinitions[2].Width = compact ? new GridLength(0) : new GridLength(0.7, GridUnitType.Star);
        Cells.ColumnDefinitions[3].Width = compact ? new GridLength(0) : GridLength.Star;
        Cells.ColumnSpacing = compact ? 0 : 16;
        var showConnection = !(compact && _follows) && (Connection.IsVisible || ConnectionHint.IsVisible);
        Cells.RowSpacing = compact ? 6 : 0;
        Cells.RowDefinitions = new RowDefinitions(compact ? showConnection ? "Auto,Auto,Auto,Auto" : "Auto,Auto,Auto" : "Auto");
        Grid.SetColumn(ModelCell, compact ? 0 : 1);
        Grid.SetColumn(EffortCell, compact ? 0 : 2);
        Grid.SetColumn(ConnectionCell, compact ? 0 : 3);
        Grid.SetRow(ModelCell, compact ? 1 : 0);
        Grid.SetRow(EffortCell, compact ? 2 : 0);
        Grid.SetRow(ConnectionCell, compact && showConnection ? 3 : 0);
        EffortCaption.IsVisible = compact && EffortPicker.IsVisible;
        ConnectionCell.IsVisible = showConnection;
    }

    /// <summary>Installs models and their unavailable or follow-default entries.</summary>
    /// <param name="items">The native choice items.</param>
    /// <param name="selected">Stored model reference.</param>
    /// <param name="enabled">Whether the model role can be changed.</param>
    /// <param name="picked">Commits a changed selection.</param>
    internal void SetModels(IReadOnlyList<ComboBoxItem> items, string selected, bool enabled, Func<string, Task> picked) =>
        Connect(ModelPicker, items, selected, enabled, picked);

    /// <summary>Installs the effort levels for this model.</summary>
    /// <param name="levels">The wire levels offered by the model.</param>
    /// <param name="selected">Stored effort.</param>
    /// <param name="enabled">Whether the role supports choosing an effort.</param>
    /// <param name="plain">Whether the unavailable value is text instead of a selector.</param>
    /// <param name="language">Interface language.</param>
    /// <param name="picked">Commits a changed effort.</param>
    internal void SetEfforts(string[]? levels, string selected, bool enabled, bool plain, InterfaceLanguage language, Func<string, Task> picked)
    {
        var supported = levels?.Any(level => level != "auto") == true;
        EffortPicker.IsVisible = !plain;
        EffortUnavailable.IsVisible = plain;
        EffortUnavailable.Text = MessageCatalog.Get("usage.not_applicable", language);
        var choices = supported
            ? (levels ?? []).Prepend("auto").Distinct(StringComparer.Ordinal)
                .Select(level => new ComboBoxItem { Tag = level, Content = level == "auto" ? MessageCatalog.Get("common.auto", language) : level }).ToArray()
            : [new ComboBoxItem { Tag = "auto", Content = EffortUnavailable.Text }];
        Connect(EffortPicker, choices, choices.Any(item => Equals(item.Tag, selected)) ? selected : "auto", enabled && supported, picked);
    }

    /// <summary>Binds a picker without treating its initial selection as an edit.</summary>
    /// <param name="picker">The settings selector.</param>
    /// <param name="items">Available values and group headings.</param>
    /// <param name="selected">Current value.</param>
    /// <param name="enabled">Whether writes are available.</param>
    /// <param name="pick">Commits a selection.</param>
    private static void Connect(ComboBox picker, IReadOnlyList<ComboBoxItem> items, string selected, bool enabled, Func<string, Task> pick)
    {
        picker.ItemsSource = items;
        picker.IsEnabled = enabled;
        picker.SelectedItem = items.FirstOrDefault(item => Equals(item.Tag, selected)) ?? items.FirstOrDefault(item => item.Tag is string);
        picker.SelectionChanged += async (_, _) =>
        {
            if (picker.IsEffectivelyEnabled && picker.SelectedItem is ComboBoxItem { Tag: string value } && value != selected)
            {
                await pick(value).ConfigureAwait(true);
            }
        };
    }
}
