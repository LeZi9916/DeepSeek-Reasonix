using System;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

public sealed partial class StudioPageClient
{
    /// <summary>Validates an imported file without writing a preset.</summary>
    /// <param name="definition">The parsed file contents.</param>
    /// <param name="token">Cancels the preview.</param>
    /// <returns>The validated definition and any diagnostics.</returns>
    public async Task<JsonObject> PreviewPresetAsync(JsonObject definition, CancellationToken token)
    {
        var request = new JsonObject { ["content"] = definition.DeepClone() };
        using var response = await _http.PostAsJsonAsync("/agent-presets/import", request, token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        return await BodyAsync<JsonObject>(response, token).ConfigureAwait(false);
    }

    /// <summary>Reads the canonical portable definition for the selected preset.</summary>
    /// <param name="id">Preset identity.</param>
    /// <param name="token">Cancels the read.</param>
    /// <returns>The exportable JSON object.</returns>
    public Task<JsonObject> ExportPresetAsync(string id, CancellationToken token) =>
        ReadAsync<JsonObject>("/agent-presets/" + Uri.EscapeDataString(id) + "/export", token);

    /// <summary>Deletes only the accepted version of a personal or project preset.</summary>
    /// <param name="id">Preset id.</param>
    /// <param name="revision">Expected revision.</param>
    /// <param name="token">Cancels the write.</param>
    /// <returns>The store acknowledgement.</returns>
    public async Task DeletePresetAsync(string id, string revision, CancellationToken token)
    {
        using var response = await _http.DeleteAsync("/agent-presets/" + Uri.EscapeDataString(id) + "?revision=" + Uri.EscapeDataString(revision), token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
    }

    /// <summary>Selects a named configuration preference at the requested scope.</summary>
    /// <param name="id">Preset identity.</param>
    /// <param name="workspace">True to select the workspace preference.</param>
    /// <param name="token">Cancels the write.</param>
    /// <returns>The acknowledgement.</returns>
    public Task SetPresetDefaultAsync(string id, bool workspace, CancellationToken token) =>
        PostAsync("/agent-presets/default", new JsonObject { ["id"] = id, ["workspace"] = workspace }, token);
}
