using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

public sealed partial class StudioPageClient
{
    /// <summary>Lists the working-tree changes known to the kernel.</summary>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The changed paths in host order.</returns>
    public async Task<IReadOnlyList<string>> ChangesAsync(CancellationToken token)
    {
        var response = await ReadAsync<JsonObject>("/changes", token).ConfigureAwait(false);
        return response["changes"]?.AsArray().Select(row => (string?)row?["path"]).OfType<string>().ToArray() ?? Array.Empty<string>();
    }

    /// <summary>Reads a working-tree diff without invoking a shell in the frontend.</summary>
    /// <param name="path">The changed path listed by the host.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The diff and its truncation flag.</returns>
    public Task<JsonObject> DiffAsync(string path, CancellationToken token)
    {
        return ReadAsync<JsonObject>("/changes/diff?path=" + Uri.EscapeDataString(path), token);
    }

    /// <summary>Asks the kernel how a workspace file can be previewed.</summary>
    /// <param name="path">Workspace path.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>Kind, version and size of the preview.</returns>
    public Task<JsonObject> PreviewInfoAsync(string path, CancellationToken token)
    {
        return ReadAsync<JsonObject>("/workspace/preview/info?path=" + Uri.EscapeDataString(path), token);
    }

    /// <summary>Reads image bytes from a granted workspace path.</summary>
    /// <param name="path">Workspace path.</param>
    /// <param name="version">Version of the metadata that selected this preview.</param>
    /// <param name="token">Cancels the read.</param>
    /// <returns>Image bytes, or null if the file changed during the read.</returns>
    public async Task<byte[]?> PreviewImageAsync(string path, string version, CancellationToken token)
    {
        using var response = await _http.GetAsync("/workspace/preview/content?path=" + Uri.EscapeDataString(path), HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        if (!response.Headers.TryGetValues("X-Preview-Version", out var actual) || !actual.Contains(version, StringComparer.Ordinal))
        {
            return null;
        }

        return await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
    }
}
