using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>The installed and discovery tabs with the scope and add menus.</summary>
internal sealed partial class ExtensionsToolbar : UserControl
{
    /// <summary>The hosts offered by the current runtime.</summary>
    private JsonObject[] _choices = [];

    /// <summary>The active extension scope.</summary>
    private JsonObject? _scope;

    /// <summary>The locale for the menus.</summary>
    private InterfaceLanguage _language;

    /// <summary>Changes the inventory shown for a particular project.</summary>
    private Func<string?, Task>? _pickScope;

    /// <summary>Initializes a new instance of the <see cref="ExtensionsToolbar"/> class.</summary>
    public ExtensionsToolbar() => InitializeComponent();

    /// <summary>Labels and connects the toolbar actions.</summary>
    /// <param name="language">Settings language.</param>
    /// <param name="market">Whether discovery is selected.</param>
    /// <param name="changeTab">Opens the selected surface.</param>
    /// <param name="addPlugin">Opens the source form, with a folder picker when requested.</param>
    /// <param name="addService">Opens the external-service source form.</param>
    internal void Open(InterfaceLanguage language, bool market, Func<bool, Task> changeTab, Func<bool, Task>? addPlugin, Func<Task>? addService)
    {
        _language = language;
        Installed.Content = Text("ext.installed");
        Market.Content = Text("ext.discover");
        MarkTab(market);
        Installed.Click += async (_, _) => await changeTab(false).ConfigureAwait(true);
        Market.Click += async (_, _) => await changeTab(true).ConfigureAwait(true);
        Installed.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Right)
            {
                e.Handled = true;
                await changeTab(true).ConfigureAwait(true);
            }
        };
        Market.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Left)
            {
                e.Handled = true;
                await changeTab(false).ConfigureAwait(true);
            }
        };
        AddCaption.Text = Text("ext.add_menu");
        AutomationProperties.SetName(Add, AddCaption.Text);
        Add.Click += (_, _) => AddMenu.IsOpen = !AddMenu.IsOpen;
        AddMenuChoice(
            "market",
            "ext.add_from_market",
            "ext.add_from_market_hint",
            "M9.937 15.5A2 2 0 0 0 8.5 14.063L2 12l6.5-2.063A2 2 0 0 0 9.937 8.5L12 2l2.063 6.5A2 2 0 0 0 15.5 9.937L22 12l-6.5 2.063a2 2 0 0 0-1.437 1.437L12 22z",
            () => changeTab(true));
        if (addPlugin is not null)
        {
            AddMenuChoice(
                "source",
                "ext.add_from_source",
                "ext.add_from_source_hint",
                "M10 13a4 4 0 0 0 6 0l4-4a4 4 0 0 0-6-6l-2 2 M14 11a4 4 0 0 0-6 0l-4 4a4 4 0 0 0 6 6l2-2",
                () => addPlugin(false));
            AddMenuChoice(
                "folder",
                "ext.add_from_folder",
                "ext.add_from_folder_hint",
                "M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z",
                () => addPlugin(true));
        }

        if (addService is not null)
        {
            AddMenuChoice(
                "service",
                "ext.add_service",
                "ext.add_service_hint",
                "M9 3v5m6-5v5 M7 8h10v3a5 5 0 0 1-5 5v5 M12 16a5 5 0 0 1-5-5V8",
                addService);
        }

        Scope.Click += (_, _) => ScopeMenu.IsOpen = !ScopeMenu.IsOpen;
        ScopedOnly.Content = Text("ext.only_those_with_exceptions");
        ScopedOnly.IsCheckedChanged += (_, _) => DrawScopeChoices();
    }

    /// <summary>Shows the current selected tab without disabling its navigation or focus.</summary>
    /// <param name="market">Whether discovery is selected.</param>
    internal void MarkTab(bool market)
    {
        Installed.Classes.Set("active", !market);
        Market.Classes.Set("active", market);
    }

    /// <summary>Locks navigation during a market write.</summary>
    /// <param name="applying">Whether the publisher operation is being applied.</param>
    internal void SetApplying(bool applying) => Installed.IsEnabled = Market.IsEnabled = Add.IsEnabled = !applying;

    /// <summary>Shows the workspace that owns the inventory.</summary>
    /// <param name="scope">Current rendered scope.</param>
    /// <param name="choices">Available project roots.</param>
    /// <param name="pick">Invoked when another project is chosen.</param>
    internal void ShowScope(JsonObject? scope, JsonObject[] choices, Func<string?, Task>? pick)
    {
        _scope = scope;
        _choices = choices;
        _pickScope = pick;
        ScopeCaption.Text = (string?)scope?["label"] ?? (string?)scope?["name"];
        Scope.IsVisible = !string.IsNullOrEmpty(ScopeCaption.Text);
        Scope.IsEnabled = pick is not null;
        foreach (var button in AddChoices.Children.OfType<Button>().Where(button => Equals(button.Tag, "service")))
        {
            button.IsVisible = (bool?)scope?["current"] != false;
        }

        DrawScopeChoices();
    }

    /// <summary>Draws a menu command using the reference icon and two lines of text.</summary>
    /// <param name="kind">Action identifier suffix.</param>
    /// <param name="title">Action label key.</param>
    /// <param name="hint">Action description key.</param>
    /// <param name="glyph">The displayed icon geometry.</param>
    /// <param name="action">The command.</param>
    private void AddMenuChoice(string kind, string title, string hint, string glyph, Func<Task> action)
    {
        var icon = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(glyph), StrokeThickness = 1.8, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round };
        icon.Bind(Shape.StrokeProperty, icon.GetResourceObservable("FaintBrush"));
        var canvas = new Canvas { Width = 24, Height = 24, Children = { icon } };
        var mark = new Viewbox { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Top, Child = canvas };
        var copy = new StackPanel { Spacing = 1 };
        var primary = new TextBlock { Text = Text(title), FontSize = 13, FontWeight = FontWeight.Medium };
        primary.Bind(TextBlock.ForegroundProperty, primary.GetResourceObservable("TextBrush"));
        var secondary = new TextBlock { Text = Text(hint), FontSize = 11, TextWrapping = TextWrapping.Wrap };
        secondary.Bind(TextBlock.ForegroundProperty, secondary.GetResourceObservable("FaintBrush"));
        copy.Children.Add(primary);
        copy.Children.Add(secondary);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("16,*"), ColumnSpacing = 9, Children = { mark, copy } };
        Grid.SetColumn(copy, 1);
        var button = new Button { Content = grid, Tag = kind, Classes = { "ext-menu-item" } };
        AutomationProperties.SetAutomationId(button, "action:extensions.add." + kind);
        AutomationProperties.SetName(button, primary.Text);
        button.Click += async (_, _) =>
        {
            AddMenu.IsOpen = false;
            await action().ConfigureAwait(true);
        };
        AddChoices.Children.Add(button);
    }

    /// <summary>Shows the workspace entries without moving the session itself.</summary>
    private void DrawScopeChoices()
    {
        ScopeChoices.Children.Clear();
        var choices = _choices.Length > 0 ? _choices : _scope is not null ? [_scope] : [];
        foreach (var item in choices.Where(item => ScopedOnly.IsChecked != true || (int?)item["overrides"] > 0))
        {
            var name = (string?)item["label"] ?? (string?)item["name"];
            var root = (string?)item["root"];
            var title = (bool?)item["current"] == true ? name + " · " + Text("common.this_session") : name;
            var button = new Button
            {
                Classes = { "ext-menu-item" },
                Content = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock { Text = title, FontSize = 13 },
                        new TextBlock { Text = root, FontSize = 11, TextWrapping = TextWrapping.Wrap },
                    },
                },
            };
            AutomationProperties.SetAutomationId(button, "action:extensions.scope.pick");
            AutomationProperties.SetName(button, name);
            button.Click += async (_, _) =>
            {
                ScopeMenu.IsOpen = false;
                if (_pickScope is { } read)
                {
                    await read((bool?)item["current"] == true ? null : root).ConfigureAwait(true);
                }
            };
            ScopeChoices.Children.Add(button);
        }
    }

    /// <summary>Reads a localized command caption.</summary>
    /// <param name="key">Catalog message.</param>
    /// <returns>Visible copy.</returns>
    private string Text(string key) => MessageCatalog.Get(key, _language);
}
