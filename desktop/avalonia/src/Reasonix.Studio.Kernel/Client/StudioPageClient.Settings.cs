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

    /// <summary>Enables or disables one installed plugin.</summary>
    /// <param name="name">Installed plugin name.</param>
    /// <param name="enabled">New state.</param>
    /// <param name="token">Cancels the write.</param>
    /// <returns>The acknowledgement and any deferred runtime reload.</returns>
    public async Task<JsonObject?> SetPluginEnabledAsync(string name, bool enabled, CancellationToken token)
    {
        var payload = new JsonObject { ["name"] = name, ["enabled"] = enabled };
        using var response = await _http.PostAsJsonAsync("/plugins/enabled", payload, token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        return response.StatusCode == System.Net.HttpStatusCode.NoContent
            ? null
            : await BodyAsync<JsonObject>(response, token).ConfigureAwait(false);
    }

    /// <summary>Reads the community package catalog.</summary>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The response from the kernel.</returns>
    public Task<JsonObject> MarketAsync(CancellationToken token) => ReadAsync<JsonObject>("/market/packages", token);

    /// <summary>Searches the community catalog with the host's filter and pagination semantics.</summary>
    /// <param name="kind">Selected capability, or all when empty.</param>
    /// <param name="query">Search phrase.</param>
    /// <param name="sort">Catalog ordering.</param>
    /// <param name="pinned">Whether to show pinned content only.</param>
    /// <param name="offset">Page offset.</param>
    /// <param name="token">Request lifetime.</param>
    /// <returns>The catalog page.</returns>
    public Task<JsonObject> MarketSearchAsync(string kind, string query, string sort, bool pinned, int offset, CancellationToken token)
    {
        var route = "/market/packages?sort=" + Uri.EscapeDataString(sort)
            + "&offset=" + offset.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (kind.Length > 0)
        {
            route += "&kind=" + Uri.EscapeDataString(kind);
        }

        if (query.Length > 0)
        {
            route += "&q=" + Uri.EscapeDataString(query);
        }

        if (pinned)
        {
            route += "&pinned=1";
        }

        return ReadAsync<JsonObject>(route, token);
    }

    /// <summary>Reads a single marketplace package and its installation state.</summary>
    /// <param name="slug">Publisher and package separated by a slash.</param>
    /// <param name="refresh">Bypasses cached details after a retry.</param>
    /// <param name="token">Cancels the read.</param>
    /// <returns>The package detail.</returns>
    public Task<JsonObject> MarketDetailAsync(string slug, bool refresh, CancellationToken token)
    {
        var route = MarketEntryRoute(slug);
        return ReadAsync<JsonObject>(refresh ? route + "?refresh=1" : route, token);
    }

    /// <summary>Reads the signed-in user's vote and the current marketplace tally.</summary>
    /// <param name="slug">Publisher and package separated by a slash.</param>
    /// <param name="token">Request lifetime.</param>
    /// <returns>The effective vote state.</returns>
    public Task<JsonObject?> MarketVoteAsync(string slug, CancellationToken token) =>
        OptionalPreferenceAsync(MarketEntryRoute(slug) + "/vote", token);

    /// <summary>Applies the user's explicit upvote, downvote, or withdrawal.</summary>
    /// <param name="slug">The marketplace package.</param>
    /// <param name="value">The vote chosen by the user.</param>
    /// <param name="token">Request lifetime.</param>
    /// <returns>The new vote and tally.</returns>
    public Task<JsonObject> SetMarketVoteAsync(string slug, int value, CancellationToken token) =>
        DataPostAsync(MarketEntryRoute(slug) + "/vote", new JsonObject { ["value"] = value }, token);

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
    /// <returns>The applied timeout configuration.</returns>
    public Task<JsonObject> SetAskTimeoutAsync(int seconds, CancellationToken token) =>
        DataPostAsync("/ask-timeout", new JsonObject { ["seconds"] = seconds }, token);

    /// <summary>Reads whether a submitted plan starts automatically.</summary>
    /// <param name="token">The read lifetime.</param>
    /// <returns>The current plan approval policy.</returns>
    public Task<JsonObject?> PlanApprovalAsync(CancellationToken token) => OptionalPreferenceAsync("/plan-approval", token);

    /// <summary>Saves whether submitted plans wait for a separate approval.</summary>
    /// <param name="automatic">Whether submitted plans start without waiting for approval.</param>
    /// <param name="token">The request lifetime.</param>
    /// <returns>The stored preference.</returns>
    public Task<JsonObject> SetPlanApprovalAsync(bool automatic, CancellationToken token) =>
        DataPostAsync("/plan-approval", new JsonObject { ["auto"] = automatic }, token);

    /// <summary>Reads the narration setting for tool calls.</summary>
    /// <param name="token">The read lifetime.</param>
    /// <returns>The stored narration level.</returns>
    public Task<JsonObject?> TurnNarrationAsync(CancellationToken token) => OptionalPreferenceAsync("/turn-narration-settings", token);

    /// <summary>Saves how much the agent narrates between tool calls.</summary>
    /// <param name="level">The selected verbosity.</param>
    /// <param name="token">The request lifetime.</param>
    /// <returns>The stored preference.</returns>
    public Task<JsonObject> SetTurnNarrationAsync(string level, CancellationToken token) =>
        DataPostAsync("/turn-narration-settings", new JsonObject { ["level"] = level }, token);

    /// <summary>Reads the progress watch preference and its thresholds.</summary>
    /// <param name="token">The read lifetime.</param>
    /// <returns>The current progress evaluation settings.</returns>
    public Task<JsonObject?> ProgressWatchAsync(CancellationToken token) => OptionalPreferenceAsync("/progress-watch", token);

    /// <summary>Saves the pause behavior and thresholds as one progress-watch configuration.</summary>
    /// <param name="pause">Whether to pause a stalled task.</param>
    /// <param name="rounds">Maximum tool rounds without progress.</param>
    /// <param name="multiple">Maximum input tokens as a context-window multiple.</param>
    /// <param name="token">The request lifetime.</param>
    /// <returns>The stored preference.</returns>
    public Task<JsonObject> SetProgressWatchAsync(bool pause, int rounds, int multiple, CancellationToken token)
    {
        var request = new JsonObject
        {
            ["pause"] = pause,
            ["rounds"] = rounds,
            ["tokenMultiple"] = multiple,
        };
        return DataPostAsync("/progress-watch", request, token);
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

    /// <summary>Forms the encoded endpoint for a publisher-qualified package.</summary>
    /// <param name="slug">Publisher and package.</param>
    /// <returns>The verified route.</returns>
    private static string MarketEntryRoute(string slug) => "/market/packages/" + MarketPackageId(slug);

    /// <summary>Encodes the two components of a publisher-qualified package.</summary>
    /// <param name="slug">Publisher and package.</param>
    /// <returns>The package identity for an endpoint.</returns>
    private static string MarketPackageId(string slug)
    {
        var parts = slug.Split('/');
        if (parts.Length != 2 || Array.Exists(parts, part => part.Length == 0 || part is "." or ".."))
        {
            throw new ArgumentException("Invalid marketplace package identity.", nameof(slug));
        }

        return Uri.EscapeDataString(parts[0]) + "/" + Uri.EscapeDataString(parts[1]);
    }
}
