using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

public sealed partial class StudioPageClient
{
    /// <summary>Reads whether this installation checks providers when a model menu opens.</summary>
    /// <param name="token">Cancels the read.</param>
    /// <returns>Whether health checks are enabled.</returns>
    public async Task<bool> ProviderHealthEnabledAsync(CancellationToken token)
    {
        var settings = await ReadAsync<JsonObject>("/providers/health/settings", token).ConfigureAwait(false);
        return (bool?)settings["enabled"] == true;
    }

    /// <summary>Checks one provider's connection and wallet.</summary>
    /// <param name="provider">Configuration entry name.</param>
    /// <param name="token">Cancels the check.</param>
    /// <returns>The provider's reading.</returns>
    public async Task<ProviderHealth> ProviderHealthAsync(string provider, CancellationToken token)
    {
        using var response = await _http.PostAsJsonAsync("/providers/health", new JsonObject { ["name"] = provider }, token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        return await BodyAsync<ProviderHealth>(response, token).ConfigureAwait(false);
    }
}
