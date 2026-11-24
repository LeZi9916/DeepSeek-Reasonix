using System;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

public sealed partial class StudioPageClient
{
    /// <summary>Stops a running call without stopping its containing turn.</summary>
    /// <param name="id">The kernel's call identifier.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The kernel's acknowledgement.</returns>
    public Task CancelToolCallAsync(string id, CancellationToken token) =>
        PostAsync("/tool-calls/" + Uri.EscapeDataString(id) + "/cancel", new JsonObject(), token);

    /// <summary>Reads the active work tracked by the current conversation.</summary>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The process list reported by the kernel.</returns>
    public Task<JsonArray> ReadProcessesAsync(CancellationToken token) => ReadAsync<JsonArray>("/processes", token);

    /// <summary>Reads the current session's recorded child runs.</summary>
    /// <param name="token">Cancels the request.</param>
    /// <returns>Runs and their originating tool calls.</returns>
    public Task<SubagentRun[]> ReadSubagentRunsAsync(CancellationToken token) =>
        ReadAsync<SubagentRun[]>("/subagents", token);

    /// <summary>Reads one bounded page of a subagent's canonical history.</summary>
    /// <param name="runId">The kernel's run identifier.</param>
    /// <param name="after">The continuation cursor returned by the previous page.</param>
    /// <param name="token">Cancels the request.</param>
    /// <returns>The next page and the current run metadata.</returns>
    public Task<SubagentTranscriptPage> ReadSubagentTranscriptAsync(string runId, int after, CancellationToken token) =>
        ReadAsync<SubagentTranscriptPage>("/subagents/" + Uri.EscapeDataString(runId) + "/transcript?after=" + after.ToString(CultureInfo.InvariantCulture), token);
}
