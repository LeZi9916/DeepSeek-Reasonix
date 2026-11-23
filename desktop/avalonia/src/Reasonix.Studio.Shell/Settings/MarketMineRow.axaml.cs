using Avalonia.Controls;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>One package in the publisher's inventory and its available actions.</summary>
internal sealed partial class MarketMineRow : UserControl
{
    /// <summary>Initializes a new instance of the <see cref="MarketMineRow"/> class.</summary>
    public MarketMineRow() => InitializeComponent();

    /// <summary>Displays the package's current registry and install state.</summary>
    /// <param name="package">Publisher-owned registry entry.</param>
    /// <param name="language">Settings language.</param>
    /// <param name="newVersion">Starts an editable draft using this entry.</param>
    /// <param name="inspect">Reads and reviews the exact current version.</param>
    /// <param name="submit">Submits the private entry and returns any displayed refusal.</param>
    internal void Open(
        JsonObject package,
        InterfaceLanguage language,
        Action<JsonObject> newVersion,
        Func<JsonObject, Task> inspect,
        Func<string, Task<string?>> submit)
    {
        var slug = (string?)package["slug"] ?? string.Empty;
        var status = (string?)package["status"] ?? string.Empty;
        var kind = (string?)package["kind"] ?? string.Empty;
        var version = (string?)package["latestVersion"];
        var installed = (string?)package["installed"]?["version"];
        var current = installed is not null && installed == version;
        var stuck = installed is not null && !current && kind == "skill";
        PackageName.Text = (string?)package["name"] ?? slug;
        Kind.Text = Text(kind switch
        {
            "skill" => "settings.skills",
            "plugin" => "ext.plugins",
            "mcp" => "persona.mcp_servers",
            "theme" => "ext.theme",
            _ => kind,
        });
        StateText.Text = Text(status switch
        {
            "pending" => "feedback.in_review",
            "active" => "ext.public",
            "rejected" => "ext.rejected",
            "hidden" => "ext.hidden",
            "private" => "ext.private",
            _ => status,
        });
        if (status.Length > 0)
        {
            StateMark.Classes.Add(status);
            StateText.Classes.Add(status);
        }

        Installed.IsVisible = installed is not null;
        Installed.Text = Text(current ? "ext.installed" : "ext.update_available");
        Summary.Text = (string?)package["summary"];
        Summary.IsVisible = !string.IsNullOrEmpty(Summary.Text);
        Identity.Text = "@" + (string?)package["handle"] + " · v" + version;
        var installs = ((int?)package["installCount"] ?? 0).ToString(CultureInfo.CurrentCulture);
        Metadata.Text = status switch
        {
            "active" => "· " + MessageCatalog.Get("ext.n_installs", language, ("n", installs)),
            "private" => "· " + Text("ext.visible_only_to_you_not"),
            _ => string.Empty,
        };
        Metadata.IsVisible = !string.IsNullOrEmpty(Metadata.Text);
        NextVersion.Content = Text("ext.publish_new_version");
        SendToReview.Content = Text("ext.submit_for_review");
        SendToReview.IsVisible = status == "private";
        Inspect.Content = Text(installed is null ? "ext.install" : "ext.see_what_the_update_installs");
        Inspect.IsVisible = !current && !stuck;
        RemoveFirst.IsVisible = stuck;
        RemoveFirst.Text = Text("ext.a_skill_is_not_overwritten");
        NextVersion.Tag = SendToReview.Tag = Inspect.Tag = slug;
        NextVersion.Click += (_, _) => newVersion(package);
        Inspect.Click += async (_, _) => await inspect(package).ConfigureAwait(true);
        SendToReview.Click += async (_, _) =>
        {
            NextVersion.IsEnabled = SendToReview.IsEnabled = false;
            SendToReview.Content = Text("ext.submitting");
            Failure.IsVisible = false;
            try
            {
                Failure.Text = await submit(slug).ConfigureAwait(true);
                Failure.IsVisible = !string.IsNullOrEmpty(Failure.Text);
            }
            finally
            {
                SendToReview.Content = Text("ext.submit_for_review");
                NextVersion.IsEnabled = SendToReview.IsEnabled = true;
            }
        };

        string Text(string key) => MessageCatalog.Get(key, language);
    }
}
