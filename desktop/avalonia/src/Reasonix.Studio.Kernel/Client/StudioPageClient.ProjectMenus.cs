using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

public sealed partial class StudioPageClient
{
    /// <summary>Reads the actual installed editor catalog when the native menu opens.</summary>
    /// <param name="token">Cancels discovery.</param><returns>The host's target catalog.</returns>
    public Task<JsonObject> WorkspaceEditorsAsync(CancellationToken token) => ReadAsync<JsonObject>("/host/open-with", token);

    /// <summary>Opens a listed project in a host-declared program.</summary>
    /// <param name="root">Listed canonical root.</param><param name="id">Host catalog identifier.</param>
    /// <param name="token">Cancels opening.</param><returns>The acknowledgement.</returns>
    public Task OpenWorkspaceWithAsync(string root, string id, CancellationToken token) => PostAsync("/host/open-with", new JsonObject { ["root"] = root, ["id"] = id }, token);

    /// <summary>Places a visible remote group before its neighboring address-book machine.</summary>
    /// <param name="host">Canonical machine name.</param><param name="before">Following machine, or empty for last.</param>
    /// <param name="token">Cancels placement.</param><returns>The acknowledgement.</returns>
    public Task PlaceRemoteHostAsync(string host, string before, CancellationToken token) => PostAsync("/remotes/" + Uri.EscapeDataString(host) + "/place", new JsonObject { ["before"] = before }, token);
}
