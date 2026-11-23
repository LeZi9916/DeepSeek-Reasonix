using Avalonia.Threading;
using Reasonix.Studio.Shell.Browser;
using System;
using System.Linq;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Refreshes idle ages and releases hidden browser guests.</summary>
    private readonly DispatcherTimer _browserSweep = new() { Interval = TimeSpan.FromMinutes(1) };

    /// <summary>Starts the window's optional idle page sweep.</summary>
    private void InitializeBrowserSleep()
    {
        _browserSweep.Tick += (_, _) => ApplyBrowserSleep();
        Closed += (_, _) => _browserSweep.Stop();
        ApplyBrowserSleep();
    }

    /// <summary>Applies the idle-page preference without closing a foreground page.</summary>
    private void ApplyBrowserSleep()
    {
        if (!_workspacePreferences.SleepIdlePages)
        {
            _browserSweep.Stop();
            return;
        }

        _browserSweep.Start();
        var now = DateTimeOffset.UtcNow;
        foreach (var page in _browserPages.Concat(_workbenchStates.Values.SelectMany(state => state.Browsers)).Distinct())
        {
            page.SleepWhenIdle(now);
        }
    }
}
