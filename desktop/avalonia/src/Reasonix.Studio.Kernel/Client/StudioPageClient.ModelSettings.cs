using System;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

public sealed partial class StudioPageClient
{
    /// <summary>Reads the models offered for all settings roles, including decision sources.</summary>
    /// <param name="token">Request lifetime.</param>
    /// <returns>The host catalog.</returns>
    public async Task<ModelChoice[]> SettingsModelsAsync(CancellationToken token) =>
        (await ReadAsync<ModelCatalog>("/models?answers=all", token).ConfigureAwait(false)).Models ?? Array.Empty<ModelChoice>();

    /// <summary>Reads persisted model roles, their effort, and the effective search model.</summary>
    /// <param name="token">Request lifetime.</param>
    /// <returns>The model-role assignments.</returns>
    public Task<JsonObject?> ModelRolesAsync(CancellationToken token) => OptionalPreferenceAsync("/roles", token);

    /// <summary>Reads profile and project routes that outrank the global role selections.</summary>
    /// <param name="token">Request lifetime.</param>
    /// <returns>Overrides indexed by role.</returns>
    public Task<JsonObject?> ModelRoleOverridesAsync(CancellationToken token) => OptionalPreferenceAsync("/roles/overrides", token);

    /// <summary>Persists one model role after the user chooses a route.</summary>
    /// <param name="role">Role identifier.</param>
    /// <param name="reference">Model reference, or an empty string for the role's default.</param>
    /// <param name="effort">Requested reasoning level when the role accepts it.</param>
    /// <param name="token">Request lifetime.</param>
    /// <returns>The kernel acknowledgement.</returns>
    public Task SetModelRoleAsync(string role, string reference, string? effort, CancellationToken token)
    {
        var request = new JsonObject { ["role"] = role, ["ref"] = reference };
        if (effort is not null)
        {
            request["effort"] = effort;
        }

        return PostAsync("/roles", request, token);
    }

    /// <summary>Lets a user profile inherit its global model role.</summary>
    /// <param name="role">Role identifier.</param>
    /// <param name="key">Profile name.</param>
    /// <param name="token">Request lifetime.</param>
    /// <returns>The kernel acknowledgement.</returns>
    public Task ClearModelRoleOverrideAsync(string role, string key, CancellationToken token) =>
        PostAsync("/roles/overrides/clear", new JsonObject { ["role"] = role, ["key"] = key }, token);

    /// <summary>Reads the effective context window and compaction threshold.</summary>
    /// <param name="token">Request lifetime.</param>
    /// <returns>The settings with effective limits.</returns>
    public Task<JsonObject?> CompactionSettingsAsync(CancellationToken token) => OptionalPreferenceAsync("/compaction", token);

    /// <summary>Saves the requested context threshold and reads the authoritative result.</summary>
    /// <param name="ratio">Share of the model window.</param>
    /// <param name="token">Request lifetime.</param>
    /// <returns>The effective settings after the save.</returns>
    public async Task<JsonObject> SetCompactRatioAsync(double ratio, CancellationToken token)
    {
        using var response = await _http.PostAsJsonAsync("/compaction/ratio", new JsonObject { ["ratio"] = ratio }, token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        return await BodyAsync<JsonObject>(response, token).ConfigureAwait(false);
    }
}
