using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

public sealed partial class StudioPageClient
{
    /// <summary>Reads the provider protocol catalog for custom service configuration.</summary>
    /// <param name="token">Cancels the read.</param>
    /// <returns>Provider protocols.</returns>
    public Task<JsonArray> ProtocolsAsync(CancellationToken token) => ReadAsync<JsonArray>("/providers/protocols", token);

    /// <summary>Reads the local hub's machine address book.</summary>
    /// <param name="token">Cancels the read.</param>
    /// <returns>The entries or null on a host without remote connections.</returns>
    public async Task<JsonArray?> RemoteHostsAsync(CancellationToken token)
    {
        using var response = await _http.GetAsync("/remotes", token).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NotImplemented)
        {
            return null;
        }

        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<JsonArray>(cancellationToken: token).ConfigureAwait(false);
    }

    /// <summary>Reads aliases which the local SSH configuration offers.</summary>
    /// <param name="token">Cancels the read.</param>
    /// <returns>Alias names.</returns>
    public Task<JsonArray> RemoteCandidatesAsync(CancellationToken token) => ReadAsync<JsonArray>("/remotes/candidates", token);

    /// <summary>Reads local WSL distributions when available.</summary>
    /// <param name="token">Cancels the read.</param>
    /// <returns>The distribution catalog.</returns>
    public Task<JsonObject> RemoteDistrosAsync(CancellationToken token) => ReadAsync<JsonObject>("/remotes/wsl", token);

    /// <summary>Saves one full address-book entry.</summary>
    /// <param name="entry">The machine and its configured connection fields.</param>
    /// <param name="token">Cancels the write.</param>
    /// <returns>The acknowledgment.</returns>
    public Task SaveRemoteAsync(JsonObject entry, CancellationToken token) => PostAsync("/remotes", entry, token);

    /// <summary>Removes an entry after the user accepts the removal.</summary>
    /// <param name="name">Saved machine name.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The acknowledgment.</returns>
    public Task RemoveRemoteAsync(string name, CancellationToken token) => PostAsync("/remotes/remove", new JsonObject { ["name"] = name }, token);

    /// <summary>Requests connection diagnostics for one selected address-book entry.</summary>
    /// <param name="name">Saved machine name.</param>
    /// <param name="token">Cancels the probe.</param>
    /// <returns>The hub's result.</returns>
    public Task<JsonObject> ProbeRemoteAsync(string name, CancellationToken token) => ReadAsync<JsonObject>("/remotes/" + Uri.EscapeDataString(name) + "/probe", token);

    /// <summary>Registers a local project and opens a runtime on the hub's canonical root.</summary>
    /// <param name="path">The chosen project root.</param>
    /// <param name="register">Whether the user picked a new folder to register.</param>
    /// <param name="session">An existing transcript to resume.</param>
    /// <param name="preset">Explicit composition for a new task.</param>
    /// <param name="token">Cancels opening.</param>
    /// <returns>The runtime descriptor.</returns>
    internal async Task<JsonObject> OpenProjectRuntimeAsync(string path, bool register, string? session, string? preset, CancellationToken token)
    {
        var root = path;
        if (register)
        {
            using var registration = await _http.PostAsJsonAsync("/tree/workspaces", new JsonObject { ["path"] = path }, token).ConfigureAwait(false);
            await KernelClient.EnsureAsync(registration, token).ConfigureAwait(false);
            var project = await BodyAsync<JsonObject>(registration, token).ConfigureAwait(false);
            root = (string?)project["root"] ?? throw new System.IO.InvalidDataException("Missing registered workspace root.");
        }

        var request = new JsonObject { ["root"] = root };
        if (!string.IsNullOrEmpty(session))
        {
            request["sessionPath"] = session;
        }
        else if (!string.IsNullOrEmpty(preset))
        {
            request["agentComposition"] = preset;
        }

        using var open = await _http.PostAsJsonAsync("/runtimes", request, token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(open, token).ConfigureAwait(false);
        return await BodyAsync<JsonObject>(open, token).ConfigureAwait(false);
    }
}
