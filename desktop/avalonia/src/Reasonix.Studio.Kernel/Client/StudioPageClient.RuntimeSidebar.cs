using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

public sealed partial class StudioPageClient
{
    /// <summary>Reads checkpoints from the explicitly selected sidebar runtime.</summary>
    /// <param name="runtime">Canonical runtime identifier.</param><param name="token">Cancels the read.</param>
    /// <returns>The kernel's available turn checkpoints.</returns>
    public Task<JsonArray> RuntimeCheckpointsAsync(string runtime, CancellationToken token) => ReadAsync<JsonArray>("/rt/" + Uri.EscapeDataString(runtime) + "/checkpoints", token);

    /// <summary>Branches the selected runtime at a kernel-provided checkpoint.</summary>
    /// <param name="runtime">Canonical runtime identifier.</param><param name="turn">Finished checkpoint turn.</param>
    /// <param name="name">Requested branch title.</param><param name="token">Cancels the write.</param><returns>The canonical branch path.</returns>
    public async Task<string> BranchRuntimeAsync(string runtime, int turn, string name, CancellationToken token)
    {
        var result = await DataPostAsync("/rt/" + Uri.EscapeDataString(runtime) + "/branch/at", new JsonObject { ["turn"] = turn, ["name"] = name }, token).ConfigureAwait(false);
        return (string?)result["path"] ?? string.Empty;
    }
}
