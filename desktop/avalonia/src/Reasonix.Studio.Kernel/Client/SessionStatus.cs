using System.Text.Json.Serialization;

namespace Reasonix.Studio.Kernel.Client;

/// <summary>
/// What the kernel says about the open session, in the fields a window draws.
/// </summary>
/// <remarks>
/// Deliberately a subset: the status body carries a great deal a first slice does
/// not draw, and a field read but never shown is one more thing to keep correct
/// while nothing depends on it.
/// </remarks>
public sealed record SessionStatus
{
    /// <summary>
    /// Gets the conversation's label, which the rail and the title show.
    /// </summary>
    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether a turn is in flight.
    /// </summary>
    /// <remarks>
    /// The kernel also reports <c>busy</c>, which holds through the delivery of the
    /// previous turn's end; a window shows this one.
    /// </remarks>
    [JsonPropertyName("running")]
    public bool Running
    {
        get; init;
    }

    /// <summary>
    /// Gets the workspace the session runs in.
    /// </summary>
    [JsonPropertyName("workspaceRoot")]
    public string WorkspaceRoot { get; init; } = string.Empty;

    /// <summary>
    /// Gets the transcript the session writes to.
    /// </summary>
    /// <remarks>
    /// A draft is kept per conversation, and this is the half of that key only
    /// the kernel knows: two conversations in one workspace differ by this path.
    /// </remarks>
    [JsonPropertyName("sessionPath")]
    public string SessionPath { get; init; } = string.Empty;

    /// <summary>
    /// Gets the model the composer is set to.
    /// </summary>
    [JsonPropertyName("modelRef")]
    public string ModelRef { get; init; } = string.Empty;

    /// <summary>
    /// Gets how many context tokens the last request used.
    /// </summary>
    [JsonPropertyName("used")]
    public long Used
    {
        get; init;
    }

    /// <summary>
    /// Gets the context window the kernel measured.
    /// </summary>
    [JsonPropertyName("window")]
    public long Window
    {
        get; init;
    }
}
