using System.Text.Json.Serialization;

namespace Reasonix.Studio.Contracts.Jobs;

/// <summary>One background job as reported by the session status endpoint.</summary>
public sealed record BackgroundJobStatus
{
    /// <summary>Gets the kernel's job identifier.</summary>
    [JsonPropertyName("id")]
    public string Id
    {
        get; init;
    } = string.Empty;

    /// <summary>Gets the short label for the command or task.</summary>
    [JsonPropertyName("label")]
    public string Label
    {
        get; init;
    } = string.Empty;

    /// <summary>Gets the reader-facing name supplied by the caller.</summary>
    [JsonPropertyName("description")]
    public string? Description
    {
        get; init;
    }

    /// <summary>Gets the complete command or task description.</summary>
    [JsonPropertyName("detail")]
    public string? Detail
    {
        get; init;
    }

    /// <summary>Gets the job's lifecycle state.</summary>
    [JsonPropertyName("status")]
    public string Status
    {
        get; init;
    } = string.Empty;

    /// <summary>Gets the Unix timestamp in milliseconds at which the job started.</summary>
    [JsonPropertyName("startedAt")]
    public long StartedAt
    {
        get; init;
    }

    /// <summary>Gets the Unix timestamp in milliseconds at which the job ended, when available.</summary>
    [JsonPropertyName("endedAt")]
    public long EndedAt
    {
        get; init;
    }

    /// <summary>Gets the exit code for a completed command, when available.</summary>
    [JsonPropertyName("exitCode")]
    public int? ExitCode
    {
        get; init;
    }

    /// <summary>Gets the last line of job output provided in the snapshot.</summary>
    [JsonPropertyName("tail")]
    public string? Tail
    {
        get; init;
    }
}
