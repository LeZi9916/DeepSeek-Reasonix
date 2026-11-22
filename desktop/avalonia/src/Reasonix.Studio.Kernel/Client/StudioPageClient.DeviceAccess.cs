using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

public sealed partial class StudioPageClient
{
    /// <summary>Reads device access only where the hub exposes the sharing surface.</summary>
    /// <param name="token">Cancels the read.</param><returns>The actual device status or null if unsupported.</returns>
    public async Task<JsonObject?> DeviceAccessAsync(CancellationToken token)
    {
        using var response = await _http.GetAsync("/share", token).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NotImplemented)
        {
            return null;
        }

        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        return await BodyAsync<JsonObject>(response, token).ConfigureAwait(false);
    }

    /// <summary>Performs one explicitly selected existing device access action.</summary>
    /// <param name="action">Open, close, offer, cloud-offer or revoke.</param><param name="body">Canonical request fields.</param>
    /// <param name="token">Cancels the operation.</param><returns>The acknowledged status or offer.</returns>
    public async Task<JsonObject> DeviceAccessActionAsync(string action, JsonObject body, CancellationToken token)
    {
        if (action is not ("open" or "close" or "offer" or "cloud-offer" or "revoke"))
        {
            throw new ArgumentOutOfRangeException(nameof(action));
        }

        using var response = await _http.PostAsJsonAsync("/share/" + action, body, token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        return await BodyAsync<JsonObject>(response, token).ConfigureAwait(false);
    }
}
