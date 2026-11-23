using Avalonia.Automation;
using Avalonia.Controls;
using Reasonix.Studio.Pages.I18n;
using System.Globalization;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Preserves each chrome control while the narrow layout gathers them.</summary>
    private void InitializeWorkspaceChrome()
    {
        InitializeDeviceAccess();
        var menu = new Flyout { Placement = Avalonia.Controls.PlacementMode.BottomEdgeAlignedRight };
        ChromeMore.Flyout = menu;
        WorkspaceStatus.StatusChanged += (host, isolated, queued, language) =>
        {
            IsolatedBadge.IsVisible = isolated;
            IsolatedBadgeLabel.Text = MessageCatalog.Get("common.isolated_worktree", language);
            HostBadge.IsVisible = host.Length > 0;
            HostBadgeLabel.Text = host;
            QueuedBadge.IsVisible = queued > 0;
            QueuedBadgeLabel.Text = MessageCatalog.Get("app.queued", language) + " " + queued.ToString(CultureInfo.InvariantCulture);
            AutomationProperties.SetName(ChromeMore, MessageCatalog.Get("common.more", language));
        };
        LayoutRoot.SizeChanged += (_, _) =>
        {
            var gathered = LayoutRoot.Bounds.Width is > 0 and <= 520;
            if (gathered == ChromeMore.IsVisible)
            {
                return;
            }

            menu.Hide();
            if (gathered)
            {
                ChromeGroup.Children.Remove(ChromeTools);
                menu.Content = ChromeTools;
            }
            else
            {
                menu.Content = null;
                ChromeGroup.Children.Add(ChromeTools);
            }

            ChromeMore.IsVisible = gathered;
            ChromeGroup.Width = gathered ? 28 : double.NaN;
        };
    }
}
