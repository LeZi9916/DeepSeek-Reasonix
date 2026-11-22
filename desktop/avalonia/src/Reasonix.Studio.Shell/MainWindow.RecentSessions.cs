using Avalonia.Controls;
using Reasonix.Studio.Pages.Rail;
using Reasonix.Studio.Shell.Workspace;
using System;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The canonical recent-history surface.</summary>
    private RecentSessionsPanel? _recentSessions;

    /// <summary>The recent target acknowledged before the dialog closes.</summary>
    private SessionOpenTarget? _recentPicked;

    /// <summary>Reads recent history independently of the currently loaded sidebar page.</summary>
    private async void OpenRecentSessions()
    {
        if ((_launchOwner ?? _engine) is not { } owner || ScaledWindow.Child is not Grid host)
        {
            return;
        }

        if (_recentSessions is null)
        {
            _recentSessions = new RecentSessionsPanel { ZIndex = 1000 };
            host.Children.Add(_recentSessions);
            Projects.ActivityChanged += _recentSessions.RefreshRuns;
            _recentSessions.Closed += async () =>
            {
                if (_closing.IsCancellationRequested)
                {
                    return;
                }

                if (_recentPicked is not { } target)
                {
                    Projects.Focus();
                    return;
                }

                _recentPicked = null;
                bool Current() => !_closing.IsCancellationRequested && _switch.Path.Length == 0 && _sidebarHost == target.Host && _sessionPath == target.SessionPath;
                await Projects.RevealRecentAsync(target, Current).ConfigureAwait(true);
                if (Current() && _activePane is { } pane)
                {
                    await ReadPaneStatusAsync(pane).ConfigureAwait(true);
                }
            };
            Closed += (_, _) => _recentSessions.Hide();
        }

        _recentPicked = null;
        await _recentSessions.OpenAsync(
            Projects.RemoteNames,
            async (machine, query, token) => machine.Length == 0
                ? await owner.TreeAsync(query, token).ConfigureAwait(true)
                : await owner.Pages.RemoteTreeAsync(machine, query, token).ConfigureAwait(true),
            async target =>
            {
                await OpenTargetAsync(target).ConfigureAwait(true);
                if (_sidebarHost != target.Host || _sessionPath != target.SessionPath)
                {
                    throw new InvalidOperationException(ErrorText.Text ?? string.Empty);
                }

                _recentPicked = target;
            },
            Language,
            HideLegacySessions,
            Projects.RunState).ConfigureAwait(true);
    }
}
