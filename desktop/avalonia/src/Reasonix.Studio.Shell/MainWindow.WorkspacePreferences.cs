using Reasonix.Studio.Shell.Host;
using System;
using System.Collections.Generic;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>The current machine's workspace presentation preferences.</summary>
    private WorkspacePreferences _workspacePreferences = new();

    /// <summary>Stores the stars of one saved transcript without changing other scopes.</summary>
    /// <param name="scope">The saved conversation identity.</param>
    /// <param name="positions">Its current starred log positions.</param>
    private void SaveMessageRailBookmarks(string scope, int[] positions)
    {
        var bookmarks = new Dictionary<string, int[]>(_workspacePreferences.MessageRailBookmarks, StringComparer.Ordinal);
        if (positions.Length == 0)
        {
            bookmarks.Remove(scope);
        }
        else
        {
            bookmarks[scope] = positions;
        }

        _workspacePreferences = _workspacePreferences with
        {
            MessageRailBookmarks = bookmarks,
        };
        if (_connectOnOpen)
        {
            _workspacePreferences.Save();
        }
    }
}
