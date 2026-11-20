using Reasonix.Studio.Contracts.Tree;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

// The sidebar's tree, over the kernel's /tree route.
//
// One read answers every project the kernel knows, each with its own page of
// conversations. The window is the request's, not the client's: the same
// endpoint serves a project sidebar and a recent-ordered page across projects,
// and the two differ only in what the query asks.
//
// The type's documentation lives beside its first declaration in KernelClient.cs.
public sealed partial class KernelClient
{
    /// <summary>
    /// Reads the projects and the conversations under them.
    /// </summary>
    /// <param name="window">The page to ask for, or null for the default window.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The projects, as the kernel orders them.</returns>
    public async Task<IReadOnlyList<TreeWorkspace>> TreeAsync(
        TreeWindow? window = null,
        CancellationToken cancellationToken = default)
    {
        var path = "/tree" + (window ?? new TreeWindow()).QueryString();
        using var response = await _http.GetAsync(path, cancellationToken).ConfigureAwait(false);
        await EnsureAsync(response, cancellationToken).ConfigureAwait(false);
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var workspaces = await JsonSerializer
            .DeserializeAsync<List<TreeWorkspace>>(body, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return workspaces ?? new List<TreeWorkspace>();
    }
}
