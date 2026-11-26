using System;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

public sealed partial class StudioPageClient
{
    /// <summary>Invokes an action declared by the current extension surface.</summary>
    /// <param name="name">The namespaced action.</param><param name="token">Request lifetime.</param>
    /// <returns>The result message supplied by the extension.</returns>
    public async Task<string> InvokeExtensionActionAsync(string name, CancellationToken token)
    {
        var result = await DataPostAsync("/extensions/action", new JsonObject { ["name"] = name }, token).ConfigureAwait(false);
        return (string?)result["message"] ?? string.Empty;
    }

    /// <summary>Submits a response to the current extension surface.</summary>
    /// <param name="plugin">The extension identity.</param><param name="surface">The form's surface identity.</param>
    /// <param name="values">The values proposed by the reader.</param><param name="token">Request lifetime.</param>
    /// <returns>The kernel acknowledgement.</returns>
    public Task SubmitExtensionFormAsync(string plugin, string surface, JsonObject values, CancellationToken token) =>
        PostAsync("/extensions/submit", new JsonObject { ["pluginId"] = plugin, ["surfaceId"] = surface, ["values"] = values }, token);

    /// <summary>Reads project scopes available to the extension manager.</summary>
    /// <param name="token">Request lifetime.</param>
    /// <returns>The project choices.</returns>
    public Task<JsonObject?> ExtensionScopesAsync(CancellationToken token) => OptionalPreferenceAsync("/capability-scope?all", token);

    /// <summary>Reads the standalone skill inventory for a workspace.</summary>
    /// <param name="root">An explicit workspace, or the current session.</param>
    /// <param name="token">Request lifetime.</param>
    /// <returns>The skills and their disclosure settings.</returns>
    public Task<JsonObject?> ExtensionSkillsAsync(string? root, CancellationToken token) =>
        OptionalPreferenceAsync(root is null ? "/skills" : "/skills?root=" + Uri.EscapeDataString(root), token);

    /// <summary>Reads external services for a workspace.</summary>
    /// <param name="root">An explicit workspace, or the current session.</param>
    /// <param name="token">Request lifetime.</param>
    /// <returns>The server inventory.</returns>
    public Task<JsonObject?> ExtensionServicesAsync(string? root, CancellationToken token) =>
        root is null ? McpInventoryAsync(token) : OptionalPreferenceAsync("/mcp?root=" + Uri.EscapeDataString(root), token);

    /// <summary>Enables a skill in the selected project.</summary>
    /// <param name="name">Skill identity.</param>
    /// <param name="enabled">Requested state.</param>
    /// <param name="root">Workspace override, if chosen explicitly.</param>
    /// <param name="token">Request lifetime.</param>
    /// <returns>The kernel acknowledgement.</returns>
    public Task SetExtensionSkillEnabledAsync(string name, bool enabled, string? root, CancellationToken token) =>
        PostAsync("/skills/enabled", ExtensionSwitch(name, enabled, root), token);

    /// <summary>Removes the standalone skill with the displayed source identity.</summary>
    /// <param name="name">Skill name.</param>
    /// <param name="scope">Layer containing the skill.</param>
    /// <param name="path">Source path returned by the catalog.</param>
    /// <param name="token">Request lifetime.</param>
    /// <returns>The acknowledgement.</returns>
    public Task RemoveExtensionSkillAsync(string name, string scope, string path, CancellationToken token) =>
        PostAsync("/skills/remove", new JsonObject { ["name"] = name, ["scope"] = scope, ["path"] = path }, token);

    /// <summary>Enables an external service in the selected project.</summary>
    /// <param name="name">Service identity.</param>
    /// <param name="enabled">Requested state.</param>
    /// <param name="root">Workspace override, if chosen explicitly.</param>
    /// <param name="token">Request lifetime.</param>
    /// <returns>The kernel acknowledgement.</returns>
    public Task SetExtensionServiceEnabledAsync(string name, bool enabled, string? root, CancellationToken token) =>
        PostAsync("/mcp/enabled", ExtensionSwitch(name, enabled, root), token);

    /// <summary>Reads the host's redacted preview for an external-service declaration.</summary>
    /// <param name="input">Configuration pasted by the user.</param>
    /// <param name="token">Request lifetime.</param>
    /// <returns>Parsed services and their disclosed risks.</returns>
    public Task<JsonObject> InspectExtensionServiceAsync(string input, CancellationToken token) =>
        DataPostAsync("/mcp/parse", new JsonObject { ["input"] = input }, token);

    /// <summary>Installs one external service after its preview is accepted.</summary>
    /// <param name="server">The service draft issued by the host.</param>
    /// <param name="scope">The explicitly chosen installation layer.</param>
    /// <param name="token">Request lifetime.</param>
    /// <returns>The connection result.</returns>
    public Task<JsonObject> InstallExtensionServiceAsync(JsonObject server, string scope, CancellationToken token) =>
        DataPostAsync("/mcp/install", new JsonObject { ["server"] = server.DeepClone(), ["scope"] = scope }, token);

    /// <summary>Reads or applies a package installation plan.</summary>
    /// <param name="apply">Whether the presented plan was accepted.</param>
    /// <param name="request">The source, identity, and any reviewed plan token.</param>
    /// <param name="token">Request lifetime.</param>
    /// <returns>The plan or the applied result.</returns>
    public Task<JsonObject> PluginPlanAsync(bool apply, JsonObject request, CancellationToken token) =>
        DataPostAsync(apply ? "/plugins/install" : "/plugins/plan", request, token);

    /// <summary>Removes one installed package.</summary>
    /// <param name="name">Package identity selected by the user.</param>
    /// <param name="token">Request lifetime.</param>
    /// <returns>The removal result.</returns>
    public async Task<JsonObject> RemovePluginAsync(string name, CancellationToken token)
    {
        using var response = await _http.DeleteAsync("/plugins/" + Uri.EscapeDataString(name), token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        return await BodyAsync<JsonObject>(response, token).ConfigureAwait(false);
    }

    /// <summary>Reads the file contents of a package export and the required variables stripped by the host.</summary>
    /// <param name="name">Package identity.</param>
    /// <param name="token">Request lifetime.</param>
    /// <returns>The archive and the names of credentials it still expects.</returns>
    public async Task<(byte[] Archive, string Required)> ExportPluginAsync(string name, CancellationToken token)
    {
        using var response = await _http.GetAsync("/plugins/" + Uri.EscapeDataString(name) + "/export", token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        var required = response.Headers.TryGetValues("X-Reasonix-Required-Env", out var names) ? string.Join(", ", names) : string.Empty;
        return (await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false), required);
    }

    /// <summary>Applies accepted extension changes to the runtime.</summary>
    /// <param name="token">Request lifetime.</param>
    /// <returns>The reload acknowledgement.</returns>
    public Task ReloadExtensionsAsync(CancellationToken token) => PostAsync("/extensions/reload", new JsonObject(), token);

    /// <summary>Creates the shared project switch request used by skills and external services.</summary>
    /// <param name="name">Capability name.</param>
    /// <param name="enabled">Requested state.</param>
    /// <param name="root">Explicit project root when away from the current session.</param>
    /// <returns>The request payload.</returns>
    private static JsonObject ExtensionSwitch(string name, bool enabled, string? root) =>
        new()
        {
            ["name"] = name,
            ["enabled"] = enabled,
            ["scope"] = "project",
            ["root"] = root,
        };
}
