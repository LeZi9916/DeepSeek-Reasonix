using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

public sealed partial class StudioPageClient
{
    /// <summary>Lists workspace entries with the user's explicit hidden-file preference.</summary>
    /// <param name="directory">Canonical directory.</param><param name="query">Search terms.</param>
    /// <param name="hidden">Whether to include hidden entries.</param><param name="token">Cancels reading.</param>
    /// <returns>The canonical file listing.</returns>
    public Task<WorkspaceListing> FilesAsync(string directory, string query, bool hidden, CancellationToken token)
        => ReadAsync<WorkspaceListing>("/workspace/files?path=" + Uri.EscapeDataString(directory) + "&q=" + Uri.EscapeDataString(query) + (hidden ? "&hidden=1" : string.Empty), token);

    /// <summary>Opens the current workspace in the configured external editor.</summary>
    /// <param name="token">Cancels opening.</param><returns>The canonical editor acknowledgement.</returns>
    public Task<JsonObject> OpenWorkspaceEditorAsync(CancellationToken token)
        => DataPostAsync("/workspace/editor", new JsonObject(), token);
}
