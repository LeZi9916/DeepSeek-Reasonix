using System;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

public sealed partial class StudioPageClient
{
    /// <summary>Submits a canonical sidebar mutation to the existing hub endpoint.</summary>
    /// <param name="operation">Declared endpoint operation.</param><param name="body">Canonical request fields.</param>
    /// <param name="token">Cancels the request.</param><returns>The acknowledgement body when present.</returns>
    public async Task<JsonObject?> WorkspaceActionAsync(string operation, JsonObject body, CancellationToken token)
    {
        if (operation is not ("sessions/rename" or "sessions/auto-name" or "sessions/export" or "sessions/remove" or "sessions/move" or "sessions/move-to" or "workspaces/remove" or "workspaces/move"))
        {
            throw new ArgumentOutOfRangeException(nameof(operation));
        }

        using var response = await _http.PostAsJsonAsync("/tree/" + operation, body, token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text)?.AsObject();
    }

    /// <summary>Requests an existing runtime's pause or close action.</summary>
    /// <param name="id">Hub-issued runtime identity.</param><param name="close">Whether to close rather than pause.</param>
    /// <param name="token">Cancels the request.</param><returns>The acknowledgement.</returns>
    public Task WorkspaceRuntimeActionAsync(string id, bool close, CancellationToken token)
        => PostAsync(close ? "/runtimes/" + Uri.EscapeDataString(id) + "/close" : "/rt/" + Uri.EscapeDataString(id) + "/cancel", new JsonObject(), token);

    /// <summary>Removes an acknowledged remote conversation or saved project entry.</summary>
    /// <param name="host">Machine name.</param><param name="kind">Sessions or workspaces.</param>
    /// <param name="path">Canonical path.</param><param name="token">Cancels the write.</param>
    /// <returns>The kernel acknowledgement.</returns>
    public Task RemoveRemoteWorkspaceItemAsync(string host, string kind, string path, CancellationToken token)
    {
        if (kind is not ("sessions" or "workspaces"))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        return PostAsync("/remotes/" + Uri.EscapeDataString(host) + "/" + kind + "/remove", new JsonObject { ["path"] = path }, token);
    }
}
