using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

public sealed partial class StudioPageClient
{
    /// <summary>Reads Automation hook entries and their owning scopes.</summary>
    /// <param name="token">The query lifetime.</param>
    /// <returns>The rules and event catalog.</returns>
    public Task<JsonObject?> HooksAsync(CancellationToken token) => OptionalPreferenceAsync("/hooks", token);

    /// <summary>Saves the rule list for a single writable scope.</summary>
    /// <param name="scope">User or project.</param>
    /// <param name="rules">The new rules for this scope.</param>
    /// <param name="token">The request lifetime.</param>
    /// <returns>The acknowledgement.</returns>
    public Task SetHooksAsync(string scope, JsonArray rules, CancellationToken token) =>
        PostAsync("/hooks", new JsonObject { ["scope"] = scope, ["hooks"] = rules }, token);

    /// <summary>Requests one explicit run of the current rule against the kernel sample.</summary>
    /// <param name="rule">The selected hook rule.</param>
    /// <param name="token">The request lifetime.</param>
    /// <returns>The exit and event verdict.</returns>
    public Task<JsonObject> TryHookAsync(JsonObject rule, CancellationToken token)
    {
        var body = new JsonObject();
        foreach (var field in new[] { "event", "match", "command", "timeout", "cwd" })
        {
            if (rule.TryGetPropertyValue(field, out var value))
            {
                body[field] = value?.DeepClone();
            }
        }

        return DataPostAsync("/hooks/dry-run", body, token);
    }

    /// <summary>Reads the scheduled jobs, recent runs and trusted choices.</summary>
    /// <param name="token">The request lifetime.</param>
    /// <returns>The schedule board.</returns>
    public Task<JsonObject?> SchedulesAsync(CancellationToken token) => OptionalPreferenceAsync("/schedules", token);

    /// <summary>Previews one proposed schedule and its limits.</summary>
    /// <param name="draft">The proposed task and schedule.</param>
    /// <param name="token">The request lifetime.</param>
    /// <returns>The reviewed configuration and its confirmation token.</returns>
    public Task<JsonObject> PreviewScheduleAsync(JsonObject draft, CancellationToken token) => DataPostAsync("/schedules/preview", draft, token);

    /// <summary>Creates the schedule with the confirmation from its preview.</summary>
    /// <param name="confirmed">The reviewed draft and confirmation.</param>
    /// <param name="token">The request lifetime.</param>
    /// <returns>The new schedule.</returns>
    public Task<JsonObject> CreateScheduleAsync(JsonObject confirmed, CancellationToken token) => DataPostAsync("/schedules", confirmed, token);

    /// <summary>Changes one schedule's lifecycle state.</summary>
    /// <param name="id">The schedule ID.</param>
    /// <param name="action">The action requested by the user.</param>
    /// <param name="token">The request lifetime.</param>
    /// <returns>The resulting schedule board.</returns>
    public Task<JsonObject> ScheduleActionAsync(string id, string action, CancellationToken token) =>
        DataPostAsync("/schedules/" + Uri.EscapeDataString(id) + "/" + action, new JsonObject(), token);

    /// <summary>Pauses or resumes the schedule board.</summary>
    /// <param name="pause">Whether to pause all runs.</param>
    /// <param name="token">The request lifetime.</param>
    /// <returns>The resulting schedule board.</returns>
    public Task<JsonObject> SetSchedulesPausedAsync(bool pause, CancellationToken token) =>
        DataPostAsync(pause ? "/schedules/pause-all" : "/schedules/resume-all", new JsonObject(), token);

    /// <summary>Reads the report from a recorded run.</summary>
    /// <param name="id">The run's trigger ID.</param>
    /// <param name="token">The request lifetime.</param>
    /// <returns>The report and pending decisions.</returns>
    public Task<JsonObject> ScheduleResultAsync(string id, CancellationToken token) =>
        ReadAsync<JsonObject>("/schedules/result?trigger=" + Uri.EscapeDataString(id), token);

    /// <summary>Marks the active project trusted following the user's decision.</summary>
    /// <param name="token">The request lifetime.</param>
    /// <returns>The acknowledgement.</returns>
    public Task TrustCurrentWorkspaceAsync(CancellationToken token) =>
        PostAsync("/workspace-trust", new JsonObject { ["trust"] = "trusted" }, token);
}
