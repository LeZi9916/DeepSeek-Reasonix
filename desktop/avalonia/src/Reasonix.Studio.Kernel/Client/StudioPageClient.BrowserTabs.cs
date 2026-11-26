using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

public sealed partial class StudioPageClient
{
    /// <summary>Reads the page identities and metadata listed by the session.</summary>
    /// <param name="token">Cancels the read.</param><returns>The current browser tabs.</returns>
    public async Task<AgentBrowserTab[]> AgentBrowserTabsAsync(CancellationToken token)
    {
        using var response = await _http.GetAsync("/browser/tabs", token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<AgentBrowserTab[]>(cancellationToken: token).ConfigureAwait(false) ?? [];
    }
}
