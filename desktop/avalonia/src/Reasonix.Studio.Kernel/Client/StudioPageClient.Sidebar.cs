using Reasonix.Studio.Contracts.Tree;
using System;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

public sealed partial class StudioPageClient
{
    /// <summary>Reads a connected machine's canonical project tree.</summary>
    /// <param name="host">Address-book name.</param>
    /// <param name="query">The requested session window.</param>
    /// <param name="token">Cancels the read.</param>
    /// <returns>The remote projects in server order.</returns>
    public Task<TreeWorkspace[]> RemoteTreeAsync(string host, TreeWindow query, CancellationToken token)
        => ReadAsync<TreeWorkspace[]>("/remotes/" + Uri.EscapeDataString(host) + "/tree" + query.QueryString(), token);

    /// <summary>Writes a remote session action through its owning machine.</summary>
    /// <param name="host">Address-book name.</param>
    /// <param name="path">Canonical transcript path.</param>
    /// <param name="action">Pin, archive or viewed.</param>
    /// <param name="value">The requested pin or archive state.</param>
    /// <param name="token">Cancels the write.</param>
    /// <returns>The kernel acknowledgement.</returns>
    public Task WriteRemoteSessionAsync(string host, string path, string action, bool value, CancellationToken token)
    {
        var body = new JsonObject { ["path"] = path };
        switch (action)
        {
            case "pin":
                body["pinned"] = value;
                break;
            case "archive":
                body["archived"] = value;
                break;
            case "viewed":
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(action));
        }

        return PostAsync("/remotes/" + Uri.EscapeDataString(host) + "/sessions/" + action, body, token);
    }

    /// <summary>Opens the selected remote workspace or transcript on the local authenticated hub.</summary>
    /// <param name="host">Address-book name.</param>
    /// <param name="workspace">Remote working directory.</param>
    /// <param name="session">Saved transcript or null for a new session.</param>
    /// <param name="preset">Explicit composition for a new session.</param>
    /// <param name="token">Cancels opening.</param>
    /// <returns>The hub-issued runtime descriptor.</returns>
    internal async Task<JsonObject> OpenRemoteRuntimeAsync(string host, string workspace, string? session, string? preset, CancellationToken token)
    {
        var body = new JsonObject { ["host"] = host, ["workspace"] = workspace, ["sessionPath"] = session ?? string.Empty };
        if (string.IsNullOrEmpty(session) && !string.IsNullOrEmpty(preset))
        {
            body["agentComposition"] = preset;
        }

        using var response = await _http.PostAsJsonAsync("/remotes/open", body, token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        return await BodyAsync<JsonObject>(response, token).ConfigureAwait(false);
    }
}
