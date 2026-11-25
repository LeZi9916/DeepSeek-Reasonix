using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Host;
using Reasonix.Studio.Shell.Settings;
using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Creates a line box with the reference type size and rhythm.</summary>
    /// <param name="text">Visible copy.</param>
    /// <param name="size">Font size.</param>
    /// <param name="line">Line height.</param>
    /// <param name="wrap">Allow additional lines.</param>
    /// <returns>The text control.</returns>
    private static TextBlock DataText(string text, double size = 13, double line = 19, bool wrap = false)
    {
        var label = new Settings.DataParagraph { Text = text, FontSize = size, LineHeight = line, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap };
        label.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBlock.FontSizeProperty)
            {
                label.LineHeight = line * label.FontSize / size;
            }
        };
        label.Bind(TextBlock.FontSizeProperty, label.GetResourceObservable("StudioType" + size.ToString(CultureInfo.InvariantCulture)));
        return label;
    }

    /// <summary>Maps a child to its grid column.</summary>
    /// <param name="grid">Parent.</param>
    /// <param name="column">Column index.</param>
    /// <param name="child">Child control.</param>
    private static void DataColumn(Grid grid, int column, Control child)
    {
        Grid.SetColumn(child, column);
        grid.Children.Add(child);
    }

    /// <summary>Creates a themed information panel.</summary>
    /// <param name="body">Panel content.</param>
    /// <param name="padding">Content inset.</param>
    /// <returns>The panel.</returns>
    private static Border DataFrame(Control body, Thickness padding)
    {
        var border = new Border { Padding = padding, Child = body, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8) };
        border.Bind(Border.BackgroundProperty, border.GetResourceObservable("SurfaceBrush"));
        border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("HairBrush"));
        return border;
    }

    /// <summary>Makes a compact native button.</summary>
    /// <param name="label">Visible label.</param>
    /// <param name="height">Button height.</param>
    /// <returns>The button.</returns>
    private static Button DataButton(string label, double height = 28) => new() { Content = label, MinWidth = 0, MinHeight = 0, Height = height, Padding = new Thickness(10, 0), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>Builds the data settings page from the machine's memory, usage, storage and backup services.</summary>
    /// <param name="column">Mounted sheet.</param>
    /// <param name="api">Owning kernel.</param>
    /// <returns>The initial reads.</returns>
    private async Task BuildDataPageAsync(StackPanel column, StudioPageClient api)
    {
        var token = _closing.Token;
        var usageCard = DataCard(column, "data.memory_and_usage", "data.what_the_agent_remembers_and");
        var memories = new Settings.MemoryWorkspace { Tag = "data.memory", Margin = new Thickness(0, 7, 0, 6) };
        usageCard.Children.Add(memories);
        memories.Open(api, Language, await api.MemoriesAsync(token).ConfigureAwait(true), await api.MemoryPolicyAsync(token).ConfigureAwait(true), DescribeError, token);
        if (TopLevel.GetTopLevel(memories) == this)
        {
            _settings.Dirty = () => memories.HasChanges;
        }

        await AddUsageAsync(usageCard, api).ConfigureAwait(true);
        var storage = DataCard(column, "data.session_storage", "data.where_sessions_and_indexes_live");
        await AddStorageAsync(storage, api).ConfigureAwait(true);
        var backup = DataCard(column, "data.backup", "data.take_your_configuration_to_another");
        await AddBackupAsync(backup, api).ConfigureAwait(true);
    }

    /// <summary>Translates one data-page label.</summary>
    /// <param name="key">Catalog key.</param>
    /// <param name="args">Message substitutions.</param>
    /// <returns>Localized copy.</returns>
    private string DataLabel(string key, params (string Name, string Value)[] args) => MessageCatalog.Get(key, Language, args);

    /// <summary>Creates the reference card header and content column.</summary>
    /// <param name="column">Overall sheet.</param>
    /// <param name="title">Heading.</param>
    /// <param name="hint">Description.</param>
    /// <returns>The card's content.</returns>
    private StackPanel DataCard(StackPanel column, string title, string hint)
    {
        var body = new StackPanel();
        var frame = new Border { Child = body };
        frame.Classes.Add("settings-card");
        AutomationProperties.SetAutomationId(frame, "class:prefs-card");
        var heading = DataText(DataLabel(title), 17, 23.796875);
        heading.FontWeight = FontWeight.SemiBold;
        var sub = DataText(DataLabel(hint), 12, 18, true);
        sub.Margin = new Thickness(0, 4, 0, 12);
        sub.Bind(TextBlock.ForegroundProperty, sub.GetResourceObservable("MutedBrush"));
        body.Children.Add(heading);
        body.Children.Add(sub);
        column.Children.Add(frame);
        return body;
    }

    /// <summary>Creates a settings group with its title, explanatory text and local content.</summary>
    /// <param name="card">Containing card.</param>
    /// <param name="key">Group heading and jump target.</param>
    /// <param name="hint">Supporting copy.</param>
    /// <returns>Group body.</returns>
    private StackPanel DataGroup(StackPanel card, string key, string hint)
    {
        var group = new StackPanel { Tag = key, Margin = new Thickness(0, 7, 0, 6) };
        group.Children.Add(DataText(DataLabel(key), 14, 18.1875));
        group.Children.Add(DataText(DataLabel(hint), 12, 16.796875, true));
        var body = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        group.Children.Add(body);
        card.Children.Add(group);
        return body;
    }

    /// <summary>Renders a label and an interactive control with the shared 52-DIP settings row geometry.</summary>
    /// <param name="card">Containing card.</param>
    /// <param name="key">Setting title.</param>
    /// <param name="hint">Setting description.</param>
    /// <param name="field">Interactive field.</param>
    private void DataRow(StackPanel card, string key, string hint, Control field)
    {
        var copy = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var name = DataText(DataLabel(key), 13, 19, true);
        name.FontWeight = FontWeight.Medium;
        var description = DataText(DataLabel(hint), 12, 18, true);
        description.Bind(TextBlock.ForegroundProperty, description.GetResourceObservable("MutedBrush"));
        AutomationProperties.SetAutomationId(name, "class:srow-name");
        AutomationProperties.SetAutomationId(description, "class:srow-desc");
        if (key == "session.where_this_session_writes")
        {
            var title = new WrapPanel { LineSpacing = 4, ItemSpacing = 8 };
            AutomationProperties.SetAutomationId(name, string.Empty);
            AutomationProperties.SetAutomationId(title, "class:srow-name");
            title.Children.Add(name);
            var tag = DataText(DataLabel("settings.this_workspace"), 11, 18);
            title.Children.Add(new Border { Child = tag, Padding = new Thickness(7, 0), Height = 18 });
            copy.Children.Add(title);
        }
        else
        {
            copy.Children.Add(name);
        }

        copy.Children.Add(description);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16 };
        row.Children.Add(copy);
        field.VerticalAlignment = VerticalAlignment.Center;
        DataColumn(row, 1, field);
        var frame = new Border { Tag = key, Child = row, Padding = new Thickness(0, 6), MinHeight = 52, BorderThickness = new Thickness(0, 1, 0, 0) };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("HairBrush"));
        AutomationProperties.SetAutomationId(frame, "class:srow");
        card.Children.Add(frame);
    }

    /// <summary>Builds data-page jumps in the reference catalog order, including account-gated backup.</summary>
    /// <param name="targets">The settings mounted in this sheet.</param>
    private void IndexDataSettings(Control[] targets)
    {
        SettingsIndex.IsVisible = true;
        SettingsLinks.Children.Clear();
        var label = DataText(DataLabel("settings.on_this_page"), 12, 17);
        label.Margin = new Thickness(0, 0, 8, 6);
        label.VerticalAlignment = VerticalAlignment.Center;
        SettingsLinks.Children.Add(label);
        foreach (var key in SettingsNavigation.Destinations["storage"])
        {
            var target = targets.FirstOrDefault(c => Equals(c.Tag, key));
            var button = DataButton(DataLabel(key), 26);
            button.Classes.Add("page-link");
            button.Margin = new Thickness(0, 0, 6, 6);
            AutomationProperties.SetAutomationId(button, "action:settings.page-index");
            button.Click += async (_, _) =>
            {
                if (target is not null)
                {
                    FocusSetting(target);
                }
                else if (key == "data.cloud_backup")
                {
                    await ShowSectionAsync("account").ConfigureAwait(true);
                }
            };
            SettingsLinks.Children.Add(button);
        }
    }
}
