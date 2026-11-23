using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>A community catalog card and its separate install-preview button.</summary>
internal sealed partial class MarketplaceCard : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="MarketplaceCard"/> class.</summary>
    public MarketplaceCard() => InitializeComponent();

    /// <summary>Shows the package facts supplied by the marketplace.</summary>
    /// <param name="package">A listed package at its reviewed version.</param>
    /// <param name="language">Settings language.</param>
    /// <param name="cached">Whether the listing could not be refreshed.</param>
    /// <param name="choose">Opens detail or the pinned plan preview.</param>
    internal void Present(JsonObject package, InterfaceLanguage language, bool cached, Func<string, bool, Task> choose)
    {
        var slug = (string?)package["slug"] ?? string.Empty;
        var kind = (string?)package["kind"] ?? string.Empty;
        Title.Text = (string?)package["name"] ?? slug;
        Author.Text = $"@{package["handle"]} · v{package["latestVersion"]}";
        Summary.Text = (string?)package["summary"];
        Verified.IsVisible = (bool?)package["verified"] == true;
        Kind.Text = Text(kind switch
        {
            "skill" => "settings.skills",
            "plugin" => "ext.plugins",
            "mcp" => "persona.mcp_servers",
            "theme" => "ext.theme",
            _ => kind,
        });
        Icon.Data = Geometry.Parse(kind switch
        {
            "skill" => "M9.937 15.5A2 2 0 0 0 8.5 14.063L2 12l6.5-2.063A2 2 0 0 0 9.937 8.5L12 2l2.063 6.5A2 2 0 0 0 15.5 9.937L22 12l-6.5 2.063a2 2 0 0 0-1.437 1.437L12 22z",
            "plugin" => "M12 2 2 7l10 5 10-5-10-5Z M2 12l10 5 10-5 M2 17l10 5 10-5",
            "theme" => "M2 12s4-7 10-7 10 7 10 7-4 7-10 7-10-7-10-7Z M15 12a3 3 0 1 0-6 0 3 3 0 1 0 6 0Z",
            _ => "M9 3v5m6-5v5 M7 8h10v3a5 5 0 0 1-5 5v5 M12 16a5 5 0 0 1-5-5V8",
        });
        var pinned = (bool?)package["pinned"];
        Pin.Text = pinned switch
        {
            true => "• " + Text("common.pinned"),
            false => Text("ext.not_pinned"),
            _ => Text("ext.pin_status_unknown"),
        };
        PinnedMark.Classes.Set("pinned", pinned == true);
        Installs.Text = MessageCatalog.Get("ext.n_installs", language, ("n", ((int?)package["installCount"] ?? 0).ToString(CultureInfo.CurrentCulture)));
        var votes = ((int?)package["upCount"] ?? 0) + ((int?)package["downCount"] ?? 0);
        Approval.Text = (double?)package["approvalRate"] is double approval
            ? MessageCatalog.Get(
                "ext.pct_positive_n_votes",
                language,
                ("pct", Math.Round(100 * approval).ToString(CultureInfo.CurrentCulture)),
                ("n", votes.ToString(CultureInfo.CurrentCulture)))
            : Text("ext.no_votes_yet");
        var installed = package["installed"] as JsonObject;
        InstalledBadge.IsVisible = installed is not null;
        InstalledBadge.Text = "• " + Text((string?)installed?["version"] == (string?)package["latestVersion"] ? "ext.installed" : "ext.update_available");
        Get.IsVisible = installed is null && pinned == true && !cached;
        Get.Content = Text("ext.install");
        Open.Tag = Get.Tag = slug;
        AutomationProperties.SetName(Open, Title.Text);
        Open.Click += async (_, _) => await choose(slug, false).ConfigureAwait(true);
        Get.Click += async (_, _) => await choose(slug, true).ConfigureAwait(true);

        string Text(string key) => MessageCatalog.Get(key, language);
    }

    /// <summary>Places both counts on the same line when the market uses full-width cards.</summary>
    /// <param name="single">Whether one card fills the grid row.</param>
    internal void SetSingleColumn(bool single)
    {
        FooterFacts.ColumnDefinitions = new ColumnDefinitions(single ? "Auto,Auto,*,Auto,Auto" : "Auto,Auto,*,Auto");
        FooterFacts.RowDefinitions = new RowDefinitions(single ? "Auto" : "Auto,Auto");
        Grid.SetColumn(Approval, single ? 4 : 0);
        Grid.SetColumnSpan(Approval, single ? 1 : 4);
        Grid.SetRow(Approval, single ? 0 : 1);
    }
}
