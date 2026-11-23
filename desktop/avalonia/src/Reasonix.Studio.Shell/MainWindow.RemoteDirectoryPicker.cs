using Reasonix.Studio.Kernel.Client;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Opens the directory picker in the current settings sheet.</summary>
    /// <param name="api">The machine book's owning host.</param><param name="host">The saved machine.</param>
    /// <param name="start">The default directory, if configured.</param><returns>The chosen directory or null.</returns>
    private async Task<string?> PickRemoteFolderAsync(StudioPageClient api, string host, string start)
    {
        if (RemoteFolders.IsVisible)
        {
            return null;
        }

        var previous = FocusManager?.GetFocusedElement();
        SettingsPanel.Classes.Add("browsing-remotes");
        try
        {
            return await RemoteFolders.PickAsync(api, host, start, Language, DescribeError, _closing.Token).ConfigureAwait(true);
        }
        finally
        {
            SettingsPanel.Classes.Remove("browsing-remotes");
            previous?.Focus();
        }
    }
}
