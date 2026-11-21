using System;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

public sealed partial class StudioPageClient
{
    /// <summary>Reads the native tray choice configured for this installation.</summary>
    /// <param name="token">Cancels the request.</param>
    /// <returns>Icon and close behavior.</returns>
    public Task<JsonObject> TrayPrefsAsync(CancellationToken token) => ReadAsync<JsonObject>("/tray/prefs", token);

    /// <summary>Reads the kernel's translated tray menu and session status.</summary>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The tray state.</returns>
    public Task<JsonObject> TrayStateAsync(CancellationToken token) => ReadAsync<JsonObject>("/tray/state", token);

    /// <summary>Changes close behavior through the tray preference owner.</summary>
    /// <param name="closeToTray">Whether closing the window hides it.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The saved preferences.</returns>
    public async Task<JsonObject> SetCloseToTrayAsync(bool closeToTray, CancellationToken token)
    {
        var request = new JsonObject { ["icon"] = true, ["closeToTray"] = closeToTray };
        using var response = await _http.PutAsJsonAsync("/tray/prefs", request, token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        return await BodyAsync<JsonObject>(response, token).ConfigureAwait(false);
    }

    /// <summary>Reads the active system persona.</summary>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The response from the kernel.</returns>
    public Task<JsonObject> PersonaAsync(CancellationToken token) => ReadAsync<JsonObject>("/persona", token);

    /// <summary>Reads the network configuration.</summary>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The response from the kernel.</returns>
    public Task<JsonObject> NetworkAsync(CancellationToken token) => ReadAsync<JsonObject>("/network", token);

    /// <summary>Reads the signed-in account summary.</summary>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The response from the kernel.</returns>
    public Task<JsonObject> AccountAsync(CancellationToken token) => ReadAsync<JsonObject>("/account", token);

    /// <summary>Reads installed extension packages.</summary>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The response from the kernel.</returns>
    public Task<JsonArray> PluginsAsync(CancellationToken token) => ReadAsync<JsonArray>("/plugins", token);

    /// <summary>Reads the community package catalog.</summary>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The response from the kernel.</returns>
    public Task<JsonObject> MarketAsync(CancellationToken token) => ReadAsync<JsonObject>("/market/packages", token);

    /// <summary>Reads a single marketplace package and its installation state.</summary>
    /// <param name="slug">Publisher and package separated by a slash.</param>
    /// <param name="refresh">Bypasses cached details after a retry.</param>
    /// <param name="token">Cancels the read.</param>
    /// <returns>The package detail.</returns>
    public Task<JsonObject> MarketDetailAsync(string slug, bool refresh, CancellationToken token)
    {
        var parts = slug.Split('/');
        if (parts.Length != 2 || Array.Exists(parts, part => part.Length == 0 || part is "." or ".."))
        {
            throw new ArgumentException("Invalid marketplace package identity.", nameof(slug));
        }

        var route = "/market/packages/" + Uri.EscapeDataString(parts[0]) + "/" + Uri.EscapeDataString(parts[1]);
        return ReadAsync<JsonObject>(refresh ? route + "?refresh=1" : route, token);
    }

    /// <summary>Reviews or installs one exact marketplace plan.</summary>
    /// <param name="apply">Whether the user has accepted the displayed plan.</param>
    /// <param name="request">Package identity and any reviewed version and digest.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The kernel's plan or install result.</returns>
    public async Task<JsonObject> MarketPlanAsync(bool apply, JsonObject request, CancellationToken token)
    {
        using var response = await _http.PostAsJsonAsync(apply ? "/market/install" : "/market/plan", request, token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        return await BodyAsync<JsonObject>(response, token).ConfigureAwait(false);
    }

    /// <summary>Reads the current question timeout and its allowed range.</summary>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The response from the kernel.</returns>
    public Task<JsonObject> AskTimeoutAsync(CancellationToken token) => ReadAsync<JsonObject>("/ask-timeout", token);

    /// <summary>Saves the question timeout to the session configuration.</summary>
    /// <param name="seconds">Timeout in seconds.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The acknowledgement.</returns>
    public Task SetAskTimeoutAsync(int seconds, CancellationToken token)
    {
        var request = new JsonObject { ["seconds"] = seconds };
        return PostAsync("/ask-timeout", request, token);
    }

    /// <summary>Saves the editable fields of a system persona.</summary>
    /// <param name="prefix">Instructions before the built-in persona.</param>
    /// <param name="suffix">Instructions after the built-in persona.</param>
    /// <param name="context">Whether runtime context accompanies the persona.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The acknowledgement.</returns>
    public Task SetPersonaAsync(string prefix, string suffix, bool context, CancellationToken token)
    {
        var request = new JsonObject
        {
            ["prefix"] = prefix,
            ["suffix"] = suffix,
            ["include_runtime_context"] = context,
        };
        return PostAsync("/persona", request, token);
    }

    /// <summary>Reads the auto-archive policy.</summary>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The stored switch and age threshold.</returns>
    public Task<JsonObject> ArchivePolicyAsync(CancellationToken token) => ReadAsync<JsonObject>("/auto-archive", token);

    /// <summary>Saves both fields required by the auto-archive endpoint.</summary>
    /// <param name="enabled">Whether automatic archiving is enabled.</param>
    /// <param name="days">Days of inactivity.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The normalized policy.</returns>
    public async Task<JsonObject> SetArchivePolicyAsync(bool enabled, int days, CancellationToken token)
    {
        var edit = new JsonObject { ["enabled"] = enabled, ["days"] = days };
        using var response = await _http.PostAsJsonAsync("/auto-archive", edit, token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        return await BodyAsync<JsonObject>(response, token).ConfigureAwait(false);
    }

    /// <summary>Saves user-edited proxy settings and returns the applied configuration.</summary>
    /// <param name="edit">The editable proxy fields.</param>
    /// <param name="token">Cancels the write.</param>
    /// <returns>The canonical network state.</returns>
    public async Task<JsonObject> SetNetworkAsync(JsonObject edit, CancellationToken token)
    {
        using var response = await _http.PostAsJsonAsync("/network", edit, token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        return await BodyAsync<JsonObject>(response, token).ConfigureAwait(false);
    }

    /// <summary>Diagnoses the saved network configuration.</summary>
    /// <param name="token">Cancels the user-requested check.</param>
    /// <returns>The checks reported by the kernel.</returns>
    public async Task<JsonObject> DiagnoseNetworkAsync(CancellationToken token)
    {
        using var response = await _http.PostAsJsonAsync("/network/diagnose", new JsonObject(), token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        return await BodyAsync<JsonObject>(response, token).ConfigureAwait(false);
    }

    /// <summary>Reads a settings block from its owning endpoint.</summary>
    /// <param name="setting">The setting to read.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The current value.</returns>
    public async Task<bool> FlagAsync(StudioSetting setting, CancellationToken token)
    {
        var (path, field) = Route(setting);
        var block = await ReadAsync<JsonObject>(path, token).ConfigureAwait(false);
        return block[field]!.GetValue<bool>();
    }

    /// <summary>Changes one setting through the endpoint used by the reference frontend.</summary>
    /// <param name="setting">The setting to save.</param>
    /// <param name="value">The value chosen by the user.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The value the kernel applied.</returns>
    public async Task<bool> SetFlagAsync(StudioSetting setting, bool value, CancellationToken token)
    {
        var (path, field) = Route(setting);
        var patch = new JsonObject { [field] = value };
        if (setting == StudioSetting.AutoArchive)
        {
            var current = await ReadAsync<JsonObject>(path, token).ConfigureAwait(false);
            patch["days"] = current["days"]?.DeepClone();
        }

        using var response = await _http.PostAsJsonAsync(path, patch, token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        var applied = await BodyAsync<JsonObject>(response, token).ConfigureAwait(false);
        return applied[field]!.GetValue<bool>();
    }

    /// <summary>Names the canonical endpoint and field for a native setting.</summary>
    /// <param name="setting">The setting.</param>
    /// <returns>The route and its writable field.</returns>
    private static (string Path, string Field) Route(StudioSetting setting) => setting switch
    {
        StudioSetting.DefaultPlan => ("/default-mode", "plan"),
        StudioSetting.FoldWork => ("/fold-work-settings", "fold"),
        StudioSetting.PresentFiles => ("/present-files", "enabled"),
        StudioSetting.ChangedFiles => ("/changed-files-settings", "show"),
        StudioSetting.ProviderHealth => ("/providers/health/settings", "enabled"),
        StudioSetting.BrowserTools => ("/browser-tools", "enabled"),
        StudioSetting.AutoArchive => ("/auto-archive", "enabled"),
        StudioSetting.TraceExport => ("/observability", "enabled"),
        _ => throw new ArgumentOutOfRangeException(nameof(setting)),
    };
}
