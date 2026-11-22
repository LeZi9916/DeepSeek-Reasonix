using Avalonia.Controls;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Host;
using Reasonix.Studio.Shell.Workspace;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Mounts the existing community information within the account destination.</summary>
    /// <param name="column">The existing account settings column.</param>
    private void CommunityControl(StackPanel column)
    {
        var card = Card(column, "common.community_and_contributors");
        card.Children.Add(Detail(MessageCatalog.Get("about.join_the_community_for_help", Language)));
        var community = new Workspace.CommunityControl();
        community.Configure(Language, new AvaloniaHost(this).OpenExternalAsync);
        card.Children.Add(community);
    }
}
