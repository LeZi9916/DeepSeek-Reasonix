using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

public sealed partial class StudioPageClient
{
    /// <summary>Reads MCP readiness for the context footer.</summary>
    /// <param name="token">Cancels the read.</param><returns>The host's server inventory.</returns>
    public Task<JsonObject?> McpInventoryAsync(CancellationToken token) => OptionalPreferenceAsync("/mcp", token);

    /// <summary>Reads the active worktree identity and isolation state.</summary>
    /// <param name="token">Cancels the read.</param><returns>The canonical workspace reading.</returns>
    public Task<JsonObject?> WorkspaceInfoAsync(CancellationToken token) => OptionalPreferenceAsync("/workspace", token);
}
