using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Edits the proxy draft and runs diagnostics against saved settings.</summary>
    /// <param name="card">The network card.</param>
    /// <param name="api">Owning kernel.</param>
    /// <returns>The initial network read.</returns>
    private async Task NetworkSettingsAsync(StackPanel card, StudioPageClient api)
    {
        var net = await api.NetworkAsync(_closing.Token).ConfigureAwait(true);
        var modes = new[]
        {
            new SectionEntry("auto", MessageCatalog.Get(MessageKey.Look.FollowTheSystem, Language)),
            new SectionEntry("custom", MessageCatalog.Get("models.set_manually", Language)),
            new SectionEntry("off", MessageCatalog.Get("models.direct", Language)),
        };
        var mode = new ComboBox { ItemsSource = modes, MinWidth = 140 };
        var effective = new TextBlock();
        var endpoint = new TextBlock { Text = (string?)net["endpoint"], TextWrapping = TextWrapping.Wrap };
        AddRow(card, MessageKey.Models.ProxyMode, mode);
        AddRow(card, "common.in_effect", effective);
        if (!string.IsNullOrEmpty(endpoint.Text))
        {
            AddRow(card, "models.test_target", endpoint);
        }

        var custom = new StackPanel();
        card.Children.Add(custom);
        var protocol = new ComboBox { ItemsSource = new[] { "http", "https", "socks5", "socks5h" } };
        var server = new TextBox { Width = 190 };
        var port = new NumericUpDown { Minimum = 1, Maximum = 65535, Increment = 1, Width = 105, FormatString = "0" };
        var username = new TextBox { Width = 190 };
        var password = new TextBox { Width = 190, PasswordChar = '●' };
        var bypass = new TextBox { Width = 190 };
        AddRow(custom, "models.protocol", protocol);
        AddRow(custom, "models.server", server);
        AddRow(custom, "common.port", port);
        AddRow(custom, "models.username_optional", username);
        AddRow(custom, "models.password_optional", password);
        AddRow(custom, "models.reach_these_addresses_directly", bypass);
        Adopt(net);
        JsonObject Draft() => new()
        {
            ["mode"] = (mode.SelectedItem as SectionEntry)?.Id,
            ["type"] = protocol.SelectedItem?.ToString(),
            ["server"] = server.Text?.Trim(),
            ["port"] = (int?)port.Value,
            ["username"] = username.Text,
            ["noProxy"] = bypass.Text,
            ["password"] = password.Text,
            ["clearPassword"] = false,
        };
        var saved = Draft();
        if (TopLevel.GetTopLevel(card) == this)
        {
            _unsavedSettings = () => !JsonNode.DeepEquals(saved, Draft());
        }

        mode.SelectionChanged += (_, _) => custom.IsVisible = mode.SelectedItem is SectionEntry { Id: "custom" };
        AddAction(card, MessageKey.Common.Save, async () =>
        {
            card.IsEnabled = false;
            try
            {
                net = await api.SetNetworkAsync(Draft(), _closing.Token).ConfigureAwait(true);
                Adopt(net);
                saved = Draft();
            }
            finally
            {
                card.IsEnabled = true;
            }
        });
        var results = new StackPanel { Spacing = 6 };
        AddAction(card, "models.test_saved_settings", async () =>
        {
            var response = await api.DiagnoseNetworkAsync(_closing.Token).ConfigureAwait(true);
            results.Children.Clear();
            foreach (var probe in response["probes"]?.AsArray() ?? new JsonArray())
            {
                results.Children.Add(Detail(string.Join(" · ", new[] { (string?)probe?["step"], (string?)probe?["detail"], (string?)probe?["advice"] }.Where(s => !string.IsNullOrEmpty(s)))));
            }
        });
        card.Children.Add(results);

        void Adopt(JsonObject applied)
        {
            mode.SelectedItem = modes.FirstOrDefault(m => m.Id == (string?)applied["mode"]);
            custom.IsVisible = mode.SelectedItem is SectionEntry { Id: "custom" };
            effective.Text = (string?)applied["effective"];
            protocol.SelectedItem = (string?)applied["type"] ?? "http";
            server.Text = (string?)applied["server"];
            port.Value = (int?)applied["port"];
            username.Text = (string?)applied["username"];
            bypass.Text = (string?)applied["noProxy"];
            password.Text = string.Empty;
        }
    }

    /// <summary>Switches between installed extensions and searchable marketplace records.</summary>
    /// <param name="column">The section body.</param>
    /// <param name="api">The kernel supplying the inventories.</param>
    /// <param name="market">Whether the discovery tab is selected.</param>
    /// <returns>The active tab read.</returns>
    private async Task PackageSettingsAsync(StackPanel column, StudioPageClient api, bool market)
    {
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        column.Children.Add(tabs);
        var installedTab = new Button { Content = MessageCatalog.Get(MessageKey.Ext.Installed, Language), IsEnabled = market };
        var marketTab = new Button { Content = MessageCatalog.Get(MessageKey.Ext.Discover, Language), IsEnabled = !market };
        AutomationProperties.SetAutomationId(installedTab, "action:extensions.tab");
        AutomationProperties.SetAutomationId(marketTab, "action:extensions.tab");
        tabs.Children.Add(installedTab);
        tabs.Children.Add(marketTab);
        installedTab.Click += async (_, _) => await ShowSectionAsync("ext").ConfigureAwait(true);
        marketTab.Click += async (_, _) => await ShowSectionAsync("ext:market").ConfigureAwait(true);
        if (!market)
        {
            var plugins = await api.PluginsAsync(_closing.Token).ConfigureAwait(true);
            var packages = Card(column, "ext.installed");
            foreach (var package in plugins.OfType<JsonObject>())
            {
                var item = new Expander { Header = (string?)package["name"] + "   " + (string?)package["version"], HorizontalAlignment = HorizontalAlignment.Stretch };
                item.Content = Detail(string.Join(Environment.NewLine, new[] { (string?)package["description"], (string?)package["source"], (string?)package["root"] }.Where(s => !string.IsNullOrEmpty(s))));
                packages.Children.Add(item);
            }

            return;
        }

        var catalog = await api.MarketAsync(_closing.Token).ConfigureAwait(true);
        var records = catalog["packages"]?.AsArray().OfType<JsonObject>().ToArray() ?? Array.Empty<JsonObject>();
        var find = new TextBox { PlaceholderText = MessageCatalog.Get("chat.search", Language) };
        column.Children.Add(find);
        var cards = new WrapPanel();
        column.Children.Add(cards);
        find.TextChanged += (_, _) => Filter();
        Filter();

        void Filter()
        {
            var query = find.Text?.Trim() ?? string.Empty;
            cards.Children.Clear();
            foreach (var package in records)
            {
                var name = (string?)package["name"] ?? (string?)package["slug"] ?? string.Empty;
                var summary = (string?)package["summary"] ?? string.Empty;
                if (!(name + " " + summary).Contains(query, StringComparison.CurrentCultureIgnoreCase))
                {
                    continue;
                }

                var content = new StackPanel { Spacing = 6 };
                content.Children.Add(new TextBlock { Text = name, FontWeight = FontWeight.SemiBold });
                content.Children.Add(Detail(summary));
                var detail = new Button { Content = MessageCatalog.Get("ext.details", Language) };
                AutomationProperties.SetAutomationId(detail, "action:market.open");
                detail.Click += async (_, _) =>
                {
                    var entry = new Settings.MarketInstall();
                    find.IsVisible = cards.IsVisible = false;
                    column.Children.Add(entry);
                    await entry.OpenAsync(api, (string)package["slug"]!, Language, DescribeError, () => ShowSectionAsync("ext:market"), _closing.Token).ConfigureAwait(true);
                };
                content.Children.Add(detail);
                var card = new Border { Width = 240, Margin = new Thickness(0, 0, 8, 8), Child = content };
                card.Classes.Add("settings-card");
                cards.Children.Add(card);
            }
        }
    }
}
