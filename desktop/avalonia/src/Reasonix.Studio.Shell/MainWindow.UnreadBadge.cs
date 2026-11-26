using Reasonix.Studio.Pages.Rail;
using Reasonix.Studio.Shell.Host;
using System.Linq;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The last unread state published to desktop surfaces.</summary>
    private bool _badgeUnread;

    /// <summary>Projects unread state from retained panes and the canonical workspace rail.</summary>
    private void ReflectUnreadBadge()
    {
        if (!_connectOnOpen)
        {
            return;
        }

        var unread = _navigation?.Panes.Any(pane => pane.Unread) == true
            || Projects.SearchEntries.Any(entry => entry.Session is { Unread: true }
                && !(IsActive && ConversationArea.IsEffectivelyVisible && entry.Target.Host == _sidebarHost
                    && RailPresenter.Same(entry.Target.SessionPath, _sessionPath)));
        if (_badgeUnread == unread)
        {
            return;
        }

        _badgeUnread = unread;
        _ = new AvaloniaHost(this) { Tray = _statusIcon }.SetUnreadBadgeAsync(unread);
    }
}
