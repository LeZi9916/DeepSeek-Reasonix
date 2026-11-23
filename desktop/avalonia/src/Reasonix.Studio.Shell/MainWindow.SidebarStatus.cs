using Avalonia.Threading;
using Reasonix.Studio.Contracts.Tree;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Pages.Rail;
using Reasonix.Studio.State;
using Reasonix.Studio.State.Transcript;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Refreshes link progress independently of conversation traffic.</summary>
    private readonly DispatcherTimer _sidebarRefresh = new() { Interval = TimeSpan.FromSeconds(3) };

    /// <summary>Last acknowledged trees retained through transient connection failures.</summary>
    private readonly Dictionary<string, TreeWorkspace[]> _remoteTrees = new(StringComparer.Ordinal);

    /// <summary>Address-book name owning the current transcript.</summary>
    private string _sidebarHost = string.Empty;

    /// <summary>Last rendered data and calendar minute.</summary>
    private string _sidebarSnapshot = string.Empty;

    /// <summary>Only the newest remote read may publish.</summary>
    private long _remoteRead;

    /// <summary>Checks whether the rail omits a configured machine on this device.</summary>
    /// <param name="name">The address-book name.</param><returns>Whether the rail hides this machine.</returns>
    private bool IsRemoteHidden(string name) => _workspacePreferences.HiddenRemoteHosts.Contains(name, StringComparer.Ordinal);

    /// <summary>Changes the device-local rail visibility and publishes a fresh remote snapshot.</summary>
    /// <param name="name">The address-book name.</param><returns>The rail update.</returns>
    private Task ToggleRemoteRailAsync(string name)
    {
        var hidden = _workspacePreferences.HiddenRemoteHosts;
        _workspacePreferences = _workspacePreferences with
        {
            HiddenRemoteHosts = IsRemoteHidden(name) ? hidden.Where(host => host != name).ToArray() : hidden.Append(name).ToArray(),
        };
        if (_connectOnOpen)
        {
            _workspacePreferences.Save();
        }

        return LoadRemoteSidebarAsync(new TreeWindow { Query = RailSearch.Text?.Trim() ?? string.Empty, HideLegacy = HideLegacySessions }, _treeRead);
    }

    /// <summary>Projects current turn state without treating an open pane as a running turn.</summary>
    /// <param name="state">The actual conversation reducer state.</param>
    private void ShowSidebarState(SessionState state)
    {
        var waiting = state.Items.Any(item => item is ApprovalItem { Open: true } or AskItem { Answered: null, Outcome: null });
        var run = waiting || state.Terminal is FailedTerminal ? "halt" : state.Running ? "running" : string.Empty;
        Projects.ShowRun(_sidebarHost, _sessionPath, run, state.Running);
    }

    /// <summary>Reads machine state and connected project books without opening a remote pane.</summary>
    /// <param name="query">Current sidebar query.</param>
    /// <param name="treeTicket">Local tree generation owning this read.</param>
    /// <returns>The acknowledged sidebar refresh.</returns>
    private async Task LoadRemoteSidebarAsync(TreeWindow query, long treeTicket)
    {
        if (_engine is not { } engine)
        {
            return;
        }

        var ticket = ++_remoteRead;
        try
        {
            var book = await engine.Pages.RemoteHostsAsync(_closing.Token).ConfigureAwait(true);
            var hosts = new List<RemoteRailHost>();
            var trees = new Dictionary<string, TreeWorkspace[]>(_remoteTrees, StringComparer.Ordinal);
            foreach (var entry in book?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
            {
                var name = (string?)entry["name"] ?? string.Empty;
                if ((bool?)entry["disabled"] == true || IsRemoteHidden(name))
                {
                    continue;
                }

                var status = (string?)entry["status"] ?? "idle";
                var error = (string?)entry["error"] ?? string.Empty;
                if (status is "connected" or "degraded")
                {
                    try
                    {
                        trees[name] = await engine.Pages.RemoteTreeAsync(name, Projects.WindowFor(query.Query, query.HideLegacy, name), _closing.Token).ConfigureAwait(true);
                    }
                    catch (OperationCanceledException) when (_closing.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception readError)
                    {
                        error = DescribeError(readError);
                    }
                }

                var projects = (trees.GetValueOrDefault(name) ?? Array.Empty<TreeWorkspace>()).ToList();
                var saved = (entry["workspaces"] as JsonArray)?.Select(root => (string?)root ?? string.Empty).ToList() ?? new List<string>();
                var primary = (string?)entry["workspace"];
                if (!string.IsNullOrEmpty(primary) && !saved.Contains(primary))
                {
                    saved.Insert(0, primary);
                }

                foreach (var root in saved.Where(root => root.Length > 0))
                {
                    if (!projects.Any(project => project.Root == root || project.WorkspaceRoots.Contains(root)))
                    {
                        projects.Add(new TreeWorkspace { Root = root, Name = root.TrimEnd('/', '\\').Split('/', '\\').Last() });
                    }
                }

                hosts.Add(new RemoteRailHost
                {
                    Name = name,
                    Target = (string?)entry["target"] ?? string.Empty,
                    Status = status,
                    Step = (string?)entry["step"] ?? string.Empty,
                    Error = error,
                    DefaultRoot = primary ?? string.Empty,
                    SavedRoots = saved,
                    Projects = projects,
                });
            }

            if (ticket != _remoteRead || treeTicket != _treeRead || !ReferenceEquals(engine, _engine) || _closing.IsCancellationRequested)
            {
                return;
            }

            var snapshot = JsonSerializer.Serialize(new
            {
                hosts,
                query.Query,
                Language,
                _sidebarHost,
                Minute = DateTimeOffset.Now.ToUnixTimeSeconds() / 60,
            });
            if (snapshot != _sidebarSnapshot)
            {
                _remoteTrees.Clear();
                foreach (var host in hosts)
                {
                    if (trees.TryGetValue(host.Name, out var tree))
                    {
                        _remoteTrees.Add(host.Name, tree);
                    }
                }

                _sidebarSnapshot = snapshot;
                Projects.ShowRemotes(hosts, query.Query, _sidebarHost);
                ObserveUnreadSpan();
            }

            ShowSidebarState(engine.State);
            if (!_sidebarRefresh.IsEnabled)
            {
                _sidebarRefresh.Start();
            }
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            // The last acknowledged snapshot remains usable until the hub can be read again.
        }
    }
}
