using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

public sealed partial class StudioPageClient
{
    /// <summary>Probes one configured provider's connection.</summary>
    /// <param name="name">Configuration entry name.</param>
    /// <param name="token">Cancels the probe.</param>
    /// <returns>The connection finding returned by the kernel.</returns>
    public async Task<JsonObject> CheckProviderAsync(string name, CancellationToken token)
    {
        using var response = await _http.PostAsJsonAsync("/providers/check", new JsonObject { ["name"] = name }, token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        return await BodyAsync<JsonObject>(response, token).ConfigureAwait(false);
    }

    /// <summary>Changes one account's display name across the configured protocols.</summary>
    /// <param name="names">Configuration names belonging to the account.</param>
    /// <param name="displayName">New label, or blank to remove it.</param>
    /// <param name="token">Cancels the write.</param>
    /// <returns>The acknowledgement.</returns>
    public Task SetProviderDisplayNameAsync(string[] names, string displayName, CancellationToken token)
    {
        var payload = new
        {
            names,
            displayName,
        };
        return PostAsync("/providers/display-name", payload, token);
    }

    /// <summary>Edits the fields owned by the model settings sheet without replacing the provider.</summary>
    /// <param name="body">Provider fields owned by the sheet.</param>
    /// <param name="token">Cancels the write.</param>
    /// <returns>The acknowledgement.</returns>
    public Task EditProviderAsync(JsonObject body, CancellationToken token) =>
        PostAsync("/providers/edit", body, token);

    /// <summary>Removes one configured provider after the caller has confirmed it.</summary>
    /// <param name="name">Configuration entry name.</param>
    /// <param name="token">Cancels the write.</param>
    /// <returns>The acknowledgement.</returns>
    public Task RemoveProviderAsync(string name, CancellationToken token) =>
        PostAsync("/providers/remove", new JsonObject { ["name"] = name }, token);

    /// <summary>Reads the provider protocol catalog for custom service configuration.</summary>
    /// <param name="token">Cancels the read.</param>
    /// <returns>Provider protocols.</returns>
    public Task<JsonArray> ProtocolsAsync(CancellationToken token) => ReadAsync<JsonArray>("/providers/protocols", token);

    /// <summary>Adds a model source without activating a model.</summary>
    /// <param name="draft">The source fields confirmed in settings.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The kernel acknowledgement.</returns>
    public Task AddProviderAsync(JsonObject draft, CancellationToken token) => PostAsync("/providers", draft, token);

    /// <summary>Reads the kernel's curated model sources.</summary>
    /// <param name="token">Cancels the read.</param>
    /// <returns>Installable source presets.</returns>
    public Task<JsonArray> ProviderPresetsAsync(CancellationToken token) => ReadAsync<JsonArray>("/providers/presets", token);

    /// <summary>Installs a curated source with the supplied account name and credential.</summary>
    /// <param name="presetId">The kernel's preset identifier.</param>
    /// <param name="name">Optional configuration name.</param>
    /// <param name="apiKey">Credential for the source.</param>
    /// <param name="token">Cancels the installation.</param>
    /// <returns>The kernel acknowledgement.</returns>
    public Task InstallProviderPresetAsync(string presetId, string name, string apiKey, CancellationToken token)
    {
        var request = new
        {
            presetId,
            name,
            apiKey,
        };
        return PostAsync("/providers/presets/install", request, token);
    }

    /// <summary>Tests one model against the address and protocol entered in settings.</summary>
    /// <param name="draft">The draft connection and model identifier.</param>
    /// <param name="token">Cancels the check.</param>
    /// <returns>The model availability result.</returns>
    public async Task<JsonObject> CheckDraftModelAsync(JsonObject draft, CancellationToken token)
    {
        using var response = await _http.PostAsJsonAsync("/providers/check/model", draft, token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        return await BodyAsync<JsonObject>(response, token).ConfigureAwait(false);
    }

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

    /// <summary>Lists folders on a saved machine using its own path syntax.</summary>
    /// <param name="name">The address-book name.</param><param name="path">The path to list, or empty for its login home.</param>
    /// <param name="token">Cancels browsing.</param><returns>The folder listing.</returns>
    public Task<JsonObject> RemoteDirectoriesAsync(string name, string path, CancellationToken token) =>
        ReadAsync<JsonObject>("/remotes/" + Uri.EscapeDataString(name) + "/dirs?path=" + Uri.EscapeDataString(path), token);

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
