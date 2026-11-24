using Reasonix.Studio.Kernel.Session;
using Reasonix.Studio.State.Transcript;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Reasonix.Studio.Kernel.Client;

/// <summary>One page of the subagent's canonical conversation.</summary>
public sealed record SubagentTranscriptPage
{
    /// <summary>Gets details of the recorded run at this read.</summary>
    [JsonPropertyName("run")]
    public SubagentRun Run
    {
        get; init;
    } = new();

    /// <summary>Gets the reader-visible messages in this page.</summary>
    [JsonPropertyName("messages")]
    public HistoryMessage[] Messages
    {
        get; init;
    } = [];

    /// <summary>Gets the continuation cursor for the next page.</summary>
    [JsonPropertyName("next")]
    public int Next
    {
        get; init;
    }

    /// <summary>Gets the end cursor of the available transcript.</summary>
    [JsonPropertyName("total")]
    public int Total
    {
        get; init;
    }

    /// <summary>Gets a value indicating whether the running record can still grow.</summary>
    [JsonPropertyName("live")]
    public bool Live
    {
        get; init;
    }

    /// <summary>Restores the accumulated history using the conversation's normal projection.</summary>
    /// <param name="messages">All pages read so far, in record order.</param>
    /// <returns>The reconstructed transcript.</returns>
    public static IReadOnlyList<TranscriptItem> Project(IReadOnlyList<HistoryMessage> messages) => HistoryProjection.Restore(messages, []);
}
