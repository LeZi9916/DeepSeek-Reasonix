using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

public sealed partial class StudioPageClient
{
    /// <summary>Requests shutdown of a background job owned by this session.</summary>
    /// <param name="id">The kernel's job identifier.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The kernel's acknowledgement.</returns>
    public Task CancelBackgroundJobAsync(string id, CancellationToken token) =>
        PostAsync("/jobs/" + Uri.EscapeDataString(id) + "/cancel", new JsonObject(), token);

    /// <summary>Reads the job's output without advancing the model's read position.</summary>
    /// <param name="id">The owning session's job identifier.</param>
    /// <param name="token">Cancels the read.</param>
    /// <returns>The current output tail and lifecycle state.</returns>
    public Task<BackgroundJobOutput> ReadBackgroundJobAsync(string id, CancellationToken token) =>
        ReadAsync<BackgroundJobOutput>("/jobs/" + Uri.EscapeDataString(id) + "/output", token);
}
