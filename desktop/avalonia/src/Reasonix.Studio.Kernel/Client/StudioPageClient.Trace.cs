using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

public sealed partial class StudioPageClient
{
    /// <summary>Reads the current trace export configuration without any credential value.</summary>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The configuration from the active runtime.</returns>
    public Task<JsonObject> TraceExportAsync(CancellationToken token) => ReadAsync<JsonObject>("/observability", token);

    /// <summary>Saves a patch using the active runtime's observability grant.</summary>
    /// <param name="patch">Only the setting being changed.</param><param name="token">Cancels the request.</param>
    /// <returns>The canonical settings after the save.</returns>
    public async Task<JsonObject> SaveTraceExportAsync(JsonObject patch, CancellationToken token)
    {
        using var response = await _http.PostAsJsonAsync("/observability", patch, token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        return await BodyAsync<JsonObject>(response, token).ConfigureAwait(false);
    }
}
