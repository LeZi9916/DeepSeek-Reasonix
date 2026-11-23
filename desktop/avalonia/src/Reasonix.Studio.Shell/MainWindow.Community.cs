using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Reasonix.Studio.Controls;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Host;
using Reasonix.Studio.Shell.Settings;
using Reasonix.Studio.Shell.Workspace;
using System;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Mounts the reference's about, community and sponsor groups.</summary>
    /// <param name="column">The existing account settings column.</param>
    /// <param name="host">The desktop's browser operation.</param>
    private void AddAccountAbout(StackPanel column, AvaloniaHost host)
    {
        var card = Card(column, "settings.about");
        card.Children.Add(Detail(MessageCatalog.Get("settings.about_note", Language)));
        var authorLabel = Detail(MessageCatalog.Get("app.author_and_maintainer", Language));
        authorLabel.VerticalAlignment = VerticalAlignment.Center;
        var author = new Button
        {
            Content = new TextBlock { Text = "esengine", [!TextBlock.FontFamilyProperty] = StudioTypography.Resource("StudioMonoFamily") },
            Classes = { "account-link" },
        };
        AutomationProperties.SetAutomationId(author, "action:community.author");
        author.Click += async (_, _) =>
        {
            try
            {
                await host.OpenExternalAsync("https://github.com/esengine").ConfigureAwait(true);
            }
            catch (Exception error)
            {
                SettingsError.Text = DescribeError(error);
            }
        };
        card.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 0, 0, 14),
            Children = { authorLabel, author },
        });

        var community = new CommunityControl();
        community.Configure(Language, host.OpenExternalAsync);
        card.Children.Add(new SettingsGroup("common.community_and_contributors", "about.join_the_community_for_help", community, Language));
        card.Children.Add(new SettingsGroup("about.support_the_project", null, new SponsorView(Language, host.OpenExternalAsync), Language));
    }
}
