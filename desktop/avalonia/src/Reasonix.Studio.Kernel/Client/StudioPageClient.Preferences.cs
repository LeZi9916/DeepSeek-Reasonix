using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

public sealed partial class StudioPageClient
{
    /// <summary>Reads the desktop update mode, when the host provides update service.</summary>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The mode, or null if unavailable.</returns>
    public async Task<string?> UpdateModeAsync(CancellationToken token)
    {
        var result = await OptionalPreferenceAsync("/studio/update-mode", token).ConfigureAwait(false);
        return (string?)result?["mode"];
    }

    /// <summary>Saves how the host checks for new versions.</summary>
    /// <param name="mode">Mode selected in the desktop controls.</param>
    /// <param name="token">Cancels the write.</param>
    /// <returns>The acknowledgement.</returns>
    public Task SetUpdateModeAsync(string mode, CancellationToken token) => PostAsync("/studio/update-mode", new JsonObject { ["mode"] = mode }, token);

    /// <summary>Reads notification preferences for this desktop.</summary>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The preferences, or null for a host without notifications.</returns>
    public Task<JsonObject?> NotificationsAsync(CancellationToken token) => OptionalPreferenceAsync("/notifications", token);

    /// <summary>Writes one notification switch, retaining the current desktop and phone preferences.</summary>
    /// <param name="key">Name of the edited field.</param>
    /// <param name="value">Value the user selected.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The accepted preference snapshot.</returns>
    public async Task<JsonObject?> SetNotificationAsync(string key, bool value, CancellationToken token)
    {
        var prefs = await NotificationsAsync(token).ConfigureAwait(false);
        if (prefs is null)
        {
            return null;
        }

        prefs[key] = value;
        using var response = await _http.PutAsJsonAsync("/notifications", prefs, token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        return await BodyAsync<JsonObject>(response, token).ConfigureAwait(false);
    }

    /// <summary>Reads the model's advertised tools and their constraints.</summary>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The current inventory.</returns>
    public Task<JsonObject> ToolsAsync(CancellationToken token) => ReadAsync<JsonObject>("/tools", token);

    /// <summary>Changes whether one tool is disabled in the user configuration.</summary>
    /// <param name="name">Canonical tool name.</param>
    /// <param name="disabled">Requested exclusion.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The resulting kernel inventory.</returns>
    public async Task<JsonObject> SetToolDisabledAsync(string name, bool disabled, CancellationToken token)
    {
        var patch = new JsonObject { ["name"] = name, ["disabled"] = disabled };
        using var response = await _http.PostAsJsonAsync("/tools/disabled", patch, token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        return await BodyAsync<JsonObject>(response, token).ConfigureAwait(false);
    }

    /// <summary>Reads installed themes and the active selection.</summary>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The theme packs.</returns>
    public Task<JsonArray> ThemesAsync(CancellationToken token) => ReadAsync<JsonArray>("/themes", token);

    /// <summary>Activates a theme for this machine.</summary>
    /// <param name="id">The installed pack id, or empty to restore Studio's default colors.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The kernel acknowledgement.</returns>
    public Task ActivateThemeAsync(string id, CancellationToken token) => PostAsync("/themes", new JsonObject { ["id"] = id }, token);

    /// <summary>Reads this machine's memory entries and unreadable files.</summary>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The memory catalog.</returns>
    public Task<JsonObject> MemoriesAsync(CancellationToken token) => ReadAsync<JsonObject>("/memory", token);

    /// <summary>Saves the editable fields of an existing memory.</summary>
    /// <param name="edit">The fact name and editable fields.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The store acknowledgement.</returns>
    public Task SaveMemoryAsync(JsonObject edit, CancellationToken token) => PostAsync("/memory/save", edit, token);

    /// <summary>Reads the audit history for a named fact.</summary>
    /// <param name="name">The fact identity.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The stored revisions.</returns>
    public async Task<JsonArray> MemoryRevisionsAsync(string name, CancellationToken token)
    {
        var history = await ReadAsync<JsonObject>("/memory/revisions?name=" + System.Uri.EscapeDataString(name), token).ConfigureAwait(false);
        return history["revisions"]?.AsArray() ?? new JsonArray();
    }

    /// <summary>Appends a new current revision from an earlier version.</summary>
    /// <param name="name">The fact identity.</param>
    /// <param name="revision">The recorded revision number.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The store acknowledgement.</returns>
    public Task RestoreMemoryAsync(string name, int revision, CancellationToken token) =>
        PostAsync("/memory/restore", new JsonObject { ["name"] = name, ["revision"] = revision }, token);

    /// <summary>Reads the agent preset definitions available to this workspace.</summary>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The preset catalog.</returns>
    public Task<JsonObject> AgentPresetsAsync(CancellationToken token) => ReadAsync<JsonObject>("/agent-presets", token);

    /// <summary>Reads the configured model providers.</summary>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The configured providers.</returns>
    public Task<JsonArray> ProvidersAsync(CancellationToken token) => ReadAsync<JsonArray>("/providers", token);

    /// <summary>Reads the host version inventory.</summary>
    /// <param name="refresh">Whether the user requested a new check.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The version information, or null if the host lacks that service.</returns>
    public Task<JsonObject?> VersionsAsync(bool refresh, CancellationToken token) => OptionalPreferenceAsync(refresh ? "/studio/versions?refresh=1" : "/studio/versions", token);

    /// <summary>Reads an optional desktop-only endpoint.</summary>
    /// <param name="path">The host route.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The preference, or null if the service is absent.</returns>
    private async Task<JsonObject?> OptionalPreferenceAsync(string path, CancellationToken token)
    {
        using var response = await _http.GetAsync(path, token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        return await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: token).ConfigureAwait(false);
    }
}
