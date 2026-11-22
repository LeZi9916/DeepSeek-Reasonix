using System.Text.Json.Serialization;

namespace Reasonix.Studio.Kernel.Client;

/// <summary>The prompt classes and capacity measured by the kernel.</summary>
public sealed record ContextBreakdown
{
    /// <summary>Gets total occupied prompt tokens.</summary>
    [JsonPropertyName("used")]
    public long Used
    {
        get; init;
    }

    /// <summary>Gets model capacity.</summary>
    [JsonPropertyName("window")]
    public long Window
    {
        get; init;
    }

    /// <summary>Gets system prompt tokens.</summary>
    [JsonPropertyName("system")]
    public long System
    {
        get; init;
    }

    /// <summary>Gets tool definition tokens.</summary>
    [JsonPropertyName("tools")]
    public long Tools
    {
        get; init;
    }

    /// <summary>Gets standing instruction tokens.</summary>
    [JsonPropertyName("project_memory")]
    public long ProjectMemory
    {
        get; init;
    }

    /// <summary>Gets the documents contributing standing instructions.</summary>
    [JsonPropertyName("project_memory_files")]
    public ContextMemoryFile[] ProjectMemoryFiles { get; init; } = [];

    /// <summary>Gets authored input tokens.</summary>
    [JsonPropertyName("user")]
    public long User
    {
        get; init;
    }

    /// <summary>Gets model reply tokens.</summary>
    [JsonPropertyName("reply")]
    public long Reply
    {
        get; init;
    }

    /// <summary>Gets tool output tokens.</summary>
    [JsonPropertyName("output")]
    public long Output
    {
        get; init;
    }

    /// <summary>Gets folded history tokens.</summary>
    [JsonPropertyName("digest")]
    public long Digest
    {
        get; init;
    }

    /// <summary>Gets the automatic maintenance threshold.</summary>
    [JsonPropertyName("compact_at")]
    public long CompactAt
    {
        get; init;
    }

    /// <summary>Gets the active capacity boundary.</summary>
    [JsonPropertyName("boundary")]
    public string Boundary { get; init; } = string.Empty;

    /// <summary>Gets the kernel's effective capacity threshold.</summary>
    [JsonPropertyName("capacity_at")]
    public long CapacityAt
    {
        get; init;
    }

    /// <summary>Gets framing and estimator overhead.</summary>
    [JsonPropertyName("overhead")]
    public long Overhead
    {
        get; init;
    }

    /// <summary>Gets the largest holders within measured classes.</summary>
    [JsonPropertyName("largest")]
    public ContextHolder[] Largest { get; init; } = [];
}
