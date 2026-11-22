using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

public sealed partial class StudioPageClient
{
    /// <summary>Reads the window's canonical open conversations.</summary>
    /// <param name="token">Cancels the read.</param>
    /// <returns>The current canonical runtime descriptors.</returns>
    public Task<RuntimeDescriptor[]> RuntimesAsync(CancellationToken token) => ReadAsync<RuntimeDescriptor[]>("/runtimes", token);

    /// <summary>Closes a runtime through its owning hub.</summary>
    /// <param name="id">Hub-issued runtime.</param><param name="token">Cancels closing.</param>
    /// <returns>The acknowledged close.</returns>
    public async Task CloseRuntimeAsync(string id, CancellationToken token)
    {
        using var reply = await _http.PostAsync("/runtimes/" + Uri.EscapeDataString(id) + "/close", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"), token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(reply, token).ConfigureAwait(false);
    }
}
