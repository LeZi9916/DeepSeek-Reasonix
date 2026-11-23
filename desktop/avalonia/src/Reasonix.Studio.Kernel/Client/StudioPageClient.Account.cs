using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

public sealed partial class StudioPageClient
{
    /// <summary>Starts the existing browser-approved device sign-in.</summary>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The short-lived device grant.</returns>
    public Task<JsonObject> BeginAccountLoginAsync(CancellationToken token) =>
        PostAccountAsync("/account/login", new JsonObject(), token);

    /// <summary>Asks whether the browser has approved this device.</summary>
    /// <param name="deviceCode">The grant returned by the host.</param><param name="token">Cancels the request.</param>
    /// <returns>The host's pending or complete state.</returns>
    public Task<JsonObject> PollAccountLoginAsync(string deviceCode, CancellationToken token) =>
        PostAccountAsync("/account/poll", new JsonObject { ["deviceCode"] = deviceCode }, token);

    /// <summary>Removes the local account credential through the owning host.</summary>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The sign-out acknowledgement.</returns>
    public Task AccountLogoutAsync(CancellationToken token) => PostAsync("/account/logout", new JsonObject(), token);

    /// <summary>Reads the log folder and the bounded list of abnormal exits.</summary>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The desktop host's log metadata.</returns>
    public Task<JsonObject> StudioLogsAsync(CancellationToken token) => ReadAsync<JsonObject>("/studio/logs", token);

    /// <summary>Posts an account action that returns a JSON state.</summary>
    /// <param name="path">The account route.</param><param name="body">Its arguments.</param><param name="token">Cancels the request.</param>
    /// <returns>The state returned by the host.</returns>
    private async Task<JsonObject> PostAccountAsync(string path, object body, CancellationToken token)
    {
        using var response = await _http.PostAsJsonAsync(path, body, token).ConfigureAwait(false);
        await KernelClient.EnsureAsync(response, token).ConfigureAwait(false);
        return await BodyAsync<JsonObject>(response, token).ConfigureAwait(false);
    }
}
