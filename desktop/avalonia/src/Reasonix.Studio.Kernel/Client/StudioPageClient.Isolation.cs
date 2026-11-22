using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

public sealed partial class StudioPageClient
{
    /// <summary>Repoints the runtime at an isolated worktree of the current repository.</summary>
    /// <param name="sourceRoot">Current workspace serving as the worktree source.</param>
    /// <param name="reference">Branch ref to check out.</param>
    /// <param name="attachments">Draft paths carried into the new worktree.</param>
    /// <param name="token">Cancels the switch.</param>
    /// <returns>The new workspace base.</returns>
    public async Task<string> IsolateWorkspaceAsync(
        string sourceRoot,
        string reference,
        IReadOnlyList<string> attachments,
        CancellationToken token)
    {
        var body = new JsonObject
        {
            ["isolate"] = true,
            ["worktree"] = new JsonObject
            {
                ["ref"] = reference,
                ["sourceRoot"] = sourceRoot,
                ["attachments"] = new JsonArray(attachments.Select(path => (JsonNode?)JsonValue.Create(path)).ToArray()),
            },
        };
        using var response = await _http.PostAsJsonAsync("/workspace", body, token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        var applied = await BodyAsync<JsonObject>(response, token).ConfigureAwait(false);
        return (string?)applied["workspaceRoot"] ?? string.Empty;
    }
}
