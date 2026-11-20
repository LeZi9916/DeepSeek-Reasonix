using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Reasonix.Studio.Contracts.Queue;

/// <summary>
/// The session's queue as the kernel holds it.
/// </summary>
/// <remarks>
/// Read back whole rather than patched: the frame that moves the queue says only
/// that it moved, and the whole answer is also what puts another window's lines —
/// and the CLI's — in front of this one.
/// </remarks>
public sealed record QueueSnapshot
{
    /// <summary>
    /// Gets the revision the queue stood at when it was read.
    /// </summary>
    [JsonPropertyName("revision")]
    public long Revision
    {
        get; init;
    }

    /// <summary>
    /// Gets a value indicating whether dispatch is held.
    /// </summary>
    [JsonPropertyName("paused")]
    public bool Paused
    {
        get; init;
    }

    /// <summary>
    /// Gets why dispatch is held, as the kernel named it.
    /// </summary>
    [JsonPropertyName("pauseCode")]
    public string PauseCode { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether a newer on-disk format loaded read-only.
    /// </summary>
    /// <remarks>
    /// Such a queue never auto-runs, so the panel must offer looking rather than
    /// editing.
    /// </remarks>
    [JsonPropertyName("readonly")]
    public bool Readonly
    {
        get; init;
    }

    /// <summary>
    /// Gets a value indicating whether the queue bodies could not be read.
    /// </summary>
    /// <remarks>
    /// It is how a caller tells an empty queue from an unreadable one: both have
    /// no items, and only one of them is a fact about the work.
    /// </remarks>
    [JsonPropertyName("unrecovered")]
    public bool Unrecovered
    {
        get; init;
    }

    /// <summary>
    /// Gets how many entries a crash left behind.
    /// </summary>
    [JsonPropertyName("recoveredCount")]
    public int RecoveredCount
    {
        get; init;
    }

    /// <summary>
    /// Gets the lines waiting, in dispatch order.
    /// </summary>
    [JsonPropertyName("items")]
    public IReadOnlyList<QueueItem> Items { get; init; } = new List<QueueItem>();

    /// <summary>
    /// Gets what is in use against both limits.
    /// </summary>
    [JsonPropertyName("capacity")]
    public QueueCapacity Capacity { get; init; } = new QueueCapacity();
}
