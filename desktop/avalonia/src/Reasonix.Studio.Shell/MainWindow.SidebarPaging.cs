using Reasonix.Studio.Contracts.Tree;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Reads a requested canonical sidebar page without discarding acknowledged rows on failure.</summary>
    /// <param name="host">Remote machine, or empty for local.</param><param name="query">Canonical window.</param>
    /// <returns>The acknowledged page projection.</returns>
    private async Task ReadSidebarWindowAsync(string host, TreeWindow query)
    {
        if (_engine is not { } engine)
        {
            return;
        }

        var ticket = ++_treeRead;
        if (host.Length > 0)
        {
            var tree = await engine.Pages.RemoteTreeAsync(host, query, _closing.Token).ConfigureAwait(true);
            if (ticket == _treeRead && ReferenceEquals(engine, _engine))
            {
                Projects.ShowRemotePage(host, tree);
            }

            return;
        }

        var local = await engine.TreeAsync(query, _closing.Token).ConfigureAwait(true);
        if (ticket == _treeRead && ReferenceEquals(engine, _engine))
        {
            ShowTree(local);
            await LoadRemoteSidebarAsync(query, ticket).ConfigureAwait(true);
        }
    }
}
