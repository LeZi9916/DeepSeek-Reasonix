using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

public sealed partial class StudioPageClient
{
    /// <summary>Has the host keep a long paste as a readable file.</summary>
    /// <param name="text">Paste body.</param>
    /// <param name="token">Cancels the write.</param>
    /// <returns>The saved file's pointer.</returns>
    public async Task<KeptText> KeepTextAsync(string text, CancellationToken token)
    {
        using var response = await _http.PostAsJsonAsync(
            "/attachments",
            new JsonObject
            {
                ["kind"] = "text",
                ["text"] = text,
            },
            token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        return await BodyAsync<KeptText>(response, token).ConfigureAwait(false);
    }
}
