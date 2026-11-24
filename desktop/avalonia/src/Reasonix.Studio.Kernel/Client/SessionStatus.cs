using Reasonix.Studio.Contracts.Jobs;
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
    /// <summary>Gets the canonical request ledger for the current session run.</summary>
    [JsonPropertyName("sessionUsage")]
    public SessionUsage? SessionUsage
    {
        get; init;
    }

    /// <summary>Gets the current aggregate cost quote and its coverage.</summary>
    [JsonPropertyName("sessionCostQuote")]
    public Contracts.Wire.CostQuote? SessionCostQuote
    {
        get; init;
    }

    /// <summary>Gets the vendor's current billing window when applicable.</summary>
    [JsonPropertyName("tariff")]
    public ModelTariff? Tariff
    {
        get; init;
    }

    /// <summary>Gets a value indicating whether the current task starts by planning.</summary>
    [JsonPropertyName("plan")]
    public bool Plan
    {
        get; init;
    }

    /// <summary>Gets the current task's execution approval boundary.</summary>
    [JsonPropertyName("toolApprovalMode")]
    public string ToolApprovalMode
    {
        get; init;
    } = "ask";

    /// <summary>Gets the current model's reasoning effort.</summary>
    [JsonPropertyName("effort")]
    public string Effort
    {
        get; init;
    } = "auto";

    /// <summary>
    /// Gets the conversation's label, which the rail and the title show.
    /// </summary>
    [JsonPropertyName("label")]
    public string Label
    {
        get; init;
    } = string.Empty;

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
    public string WorkspaceRoot
    {
        get; init;
    } = string.Empty;

    /// <summary>Gets the active session's working directory.</summary>
    [JsonPropertyName("cwd")]
    public string? Cwd
    {
        get; init;
    }

    /// <summary>
    /// Gets the transcript the session writes to.
    /// </summary>
    /// <remarks>
    /// A draft is kept per conversation, and this is the half of that key only
    /// the kernel knows: two conversations in one workspace differ by this path.
    /// </remarks>
    [JsonPropertyName("sessionPath")]
    public string SessionPath
    {
        get; init;
    } = string.Empty;

    /// <summary>
    /// Gets the model the composer is set to.
    /// </summary>
    [JsonPropertyName("modelRef")]
    public string ModelRef
    {
        get; init;
    } = string.Empty;

    /// <summary>Gets the provider's display name for the active model.</summary>
    [JsonPropertyName("providerDisplayName")]
    public string ProviderDisplayName
    {
        get; init;
    } = string.Empty;

    /// <summary>Gets a value indicating whether the active model reads images.</summary>
    [JsonPropertyName("vision")]
    public bool Vision
    {
        get; init;
    }

    /// <summary>Gets the optional modes the active model declares.</summary>
    [JsonPropertyName("modes")]
    public ModelModeChoice[] Modes
    {
        get; init;
    } = Array.Empty<ModelModeChoice>();

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

    /// <summary>Gets the background jobs still running in this session.</summary>
    [JsonPropertyName("jobs")]
    public BackgroundJobStatus[]? Jobs
    {
        get; init;
    }

    /// <summary>Gets the background jobs that already ended in this session.</summary>
    [JsonPropertyName("endedJobs")]
    public BackgroundJobStatus[]? EndedJobs
    {
        get; init;
    }
}
