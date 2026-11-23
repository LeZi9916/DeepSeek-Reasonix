using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Kernel.Client;

public sealed partial class StudioPageClient
{
    /// <summary>Reads the token limit for past conversations quoted into a message.</summary>
    /// <param name="token">The request lifetime.</param>
    /// <returns>The effective limit and its allowed range.</returns>
    public Task<JsonObject?> SessionReferenceAsync(CancellationToken token) => OptionalPreferenceAsync("/session-ref-settings", token);

    /// <summary>Changes the reference limit, with zero restoring the kernel default.</summary>
    /// <param name="tokens">The limit chosen by the user.</param>
    /// <param name="token">The request lifetime.</param>
    /// <returns>The resulting setting.</returns>
    public Task<JsonObject> SetSessionReferenceAsync(int tokens, CancellationToken token) =>
        DataPostAsync("/session-ref-settings", new JsonObject { ["tokens"] = tokens }, token);

    /// <summary>Reads the default write scope for subagents.</summary>
    /// <param name="token">The request lifetime.</param>
    /// <returns>The effective preference.</returns>
    public Task<JsonObject?> SubagentClaimsAsync(CancellationToken token) => OptionalPreferenceAsync("/subagent-claims", token);

    /// <summary>Saves the subagent write scope.</summary>
    /// <param name="optimistic">Whether a task without explicit paths starts with a per-task scope.</param>
    /// <param name="token">The request lifetime.</param>
    /// <returns>The resulting setting.</returns>
    public Task<JsonObject> SetSubagentClaimsAsync(bool optimistic, CancellationToken token) =>
        DataPostAsync("/subagent-claims", new JsonObject { ["optimistic"] = optimistic }, token);

    /// <summary>Reads the shared settings for local session messages.</summary>
    /// <param name="token">The request lifetime.</param>
    /// <returns>The settings and released projects.</returns>
    public Task<JsonObject?> PeerSettingsAsync(CancellationToken token) => OptionalPreferenceAsync("/peers/settings", token);

    /// <summary>Applies only the named edits to the session message settings.</summary>
    /// <param name="edit">The fields that changed.</param>
    /// <param name="token">The request lifetime.</param>
    /// <returns>The settings saved by the kernel.</returns>
    public Task<JsonObject> SetPeerSettingsAsync(JsonObject edit, CancellationToken token) => DataPostAsync("/peers/settings", edit, token);

    /// <summary>Reads send targets and held messages for the active session.</summary>
    /// <param name="token">The request lifetime.</param>
    /// <returns>The live peer state.</returns>
    public Task<JsonObject?> PeerStateAsync(CancellationToken token) => OptionalPreferenceAsync("/peers", token);

    /// <summary>Sends user-entered text to one local session.</summary>
    /// <param name="target">The receiving session key.</param>
    /// <param name="text">The exact message.</param>
    /// <param name="token">The request lifetime.</param>
    /// <returns>The delivery receipt.</returns>
    public Task<JsonObject> SendPeerMessageAsync(string target, string text, CancellationToken token) =>
        DataPostAsync("/peers/send", new JsonObject { ["to"] = target, ["text"] = text }, token);

    /// <summary>Decides one held message without granting subsequent messages permanent approval.</summary>
    /// <param name="id">The held message ID.</param>
    /// <param name="approve">Whether to release the message to the model.</param>
    /// <param name="token">The request lifetime.</param>
    /// <returns>The acknowledgement.</returns>
    public Task ResolvePeerMessageAsync(string id, bool approve, CancellationToken token) =>
        PostAsync("/peers/held/" + Uri.EscapeDataString(id), new JsonObject { ["approve"] = approve, ["remember"] = "none" }, token);
}
