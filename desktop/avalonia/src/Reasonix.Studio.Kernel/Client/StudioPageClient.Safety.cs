using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

public sealed partial class StudioPageClient
{
    /// <summary>Reads explicit rules and the session's active approval grants.</summary>
    /// <param name="token">The query lifetime.</param>
    /// <returns>The current permission configuration.</returns>
    public Task<JsonObject> PermissionsAsync(CancellationToken token) => ReadAsync<JsonObject>("/permissions", token);

    /// <summary>Saves explicitly edited rule lists and fallback preferences.</summary>
    /// <param name="settings">The fields accepted by the permissions editor.</param>
    /// <param name="token">The edit lifetime.</param>
    /// <returns>The updated permission state.</returns>
    public Task<JsonObject> SavePermissionsAsync(JsonObject settings, CancellationToken token) => DataPostAsync("/permissions", settings, token);

    /// <summary>Takes back a previously approved permission.</summary>
    /// <param name="rule">The exact grant, or an empty session grant to revoke all session grants.</param>
    /// <param name="remembered">Whether the grant is persisted for this project.</param>
    /// <param name="token">The request lifetime.</param>
    /// <returns>The remaining rules and grants.</returns>
    public Task<JsonObject> RevokePermissionAsync(string rule, bool remembered, CancellationToken token) =>
        DataPostAsync(remembered ? "/permissions/remembered/revoke" : "/permissions/revoke", new JsonObject { ["rule"] = rule }, token);

    /// <summary>Reads the shell confinement and writable roots.</summary>
    /// <param name="token">The read lifetime.</param>
    /// <returns>The kernel's settings response.</returns>
    public Task<JsonObject> SandboxAsync(CancellationToken token) => ReadAsync<JsonObject>("/sandbox", token);

    /// <summary>Reads the roots scoped to this project.</summary>
    /// <param name="token">The read lifetime.</param>
    /// <returns>The kernel's settings response.</returns>
    public Task<JsonObject> WorkspaceRootsAsync(CancellationToken token) => ReadAsync<JsonObject>("/workspace/roots", token);

    /// <summary>Reads the available shells and active interpreter.</summary>
    /// <param name="token">The read lifetime.</param>
    /// <returns>The kernel's settings response.</returns>
    public Task<JsonObject> ShellAsync(CancellationToken token) => ReadAsync<JsonObject>("/shell", token);

    /// <summary>Reads the parallel writer preference.</summary>
    /// <param name="token">The read lifetime.</param>
    /// <returns>The kernel's settings response.</returns>
    public Task<JsonObject> WriteLeaseAsync(CancellationToken token) => ReadAsync<JsonObject>("/write-lease", token);

    /// <summary>Reads the current web backend configuration.</summary>
    /// <param name="token">The read lifetime.</param>
    /// <returns>The kernel's settings response.</returns>
    public Task<JsonObject> WebAccessAsync(CancellationToken token) => ReadAsync<JsonObject>("/web-access-settings", token);

    /// <summary>Reads the saved and effective browser toggle.</summary>
    /// <param name="token">The read lifetime.</param>
    /// <returns>The kernel's settings response.</returns>
    public Task<JsonObject> BrowserToolsAsync(CancellationToken token) => ReadAsync<JsonObject>("/browser-tools", token);

    /// <summary>Saves the editable sandbox settings.</summary>
    /// <param name="edit">The fields changed by the user.</param>
    /// <param name="token">The write lifetime.</param>
    /// <returns>The kernel's updated setting.</returns>
    public Task<JsonObject> SaveSandboxAsync(JsonObject edit, CancellationToken token) => DataPostAsync("/sandbox", edit, token);

    /// <summary>Saves the selected interpreter and launcher configuration.</summary>
    /// <param name="edit">The fields changed by the user.</param>
    /// <param name="token">The write lifetime.</param>
    /// <returns>The kernel's updated setting.</returns>
    public Task<JsonObject> SaveShellAsync(JsonObject edit, CancellationToken token) => DataPostAsync("/shell", edit, token);

    /// <summary>Saves the changed writer preference.</summary>
    /// <param name="edit">The fields changed by the user.</param>
    /// <param name="token">The write lifetime.</param>
    /// <returns>The kernel's updated setting.</returns>
    public Task<JsonObject> SaveWriteLeaseAsync(JsonObject edit, CancellationToken token) => DataPostAsync("/write-lease", edit, token);

    /// <summary>Saves the selected web backend settings.</summary>
    /// <param name="edit">The fields changed by the user.</param>
    /// <param name="token">The write lifetime.</param>
    /// <returns>The kernel's updated setting.</returns>
    public Task<JsonObject> SaveWebAccessAsync(JsonObject edit, CancellationToken token) => DataPostAsync("/web-access-settings", edit, token);

    /// <summary>Changes one extra writable folder for the current project.</summary>
    /// <param name="path">The selected folder.</param>
    /// <param name="add">Whether to grant or remove write access.</param>
    /// <param name="token">The write lifetime.</param>
    /// <returns>The project's current writable folders.</returns>
    public Task<JsonObject> ChangeWorkspaceRootAsync(string path, bool add, CancellationToken token) =>
        DataPostAsync(add ? "/workspace/roots" : "/workspace/roots/remove", new JsonObject { ["path"] = path }, token);

    /// <summary>Saves whether browser tools are enabled in the user configuration.</summary>
    /// <param name="enabled">The selected value.</param>
    /// <param name="token">The write lifetime.</param>
    /// <returns>The saved and effective values.</returns>
    public Task<JsonObject> SaveBrowserToolsAsync(bool enabled, CancellationToken token) =>
        DataPostAsync("/browser-tools", new JsonObject { ["enabled"] = enabled }, token);

    /// <summary>Tests the saved SearXNG backend on the user's request.</summary>
    /// <param name="token">The probe lifetime.</param>
    /// <returns>The kernel's probe result.</returns>
    public Task<JsonObject> ProbeSearxngAsync(CancellationToken token) => DataPostAsync("/web-access-settings/probe", new JsonObject(), token);
}
