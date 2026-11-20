using System.Text.Json.Serialization;

namespace Reasonix.Studio.Kernel.Client;

/// <summary>
/// What the kernel answers after admitting a line into the session's queue.
/// </summary>
/// <remarks>
/// The identity is the point: a steer the kernel accepted has an entry the
/// transcript row can be joined to, and a follow-up has one that the queue strip
/// draws. <see cref="Paused"/> says dispatch is held, which is not the same as
/// the line having been refused — it waits.
/// </remarks>
public sealed record InboxReceipt
{
    /// <summary>
    /// Gets the durable entry this line became.
    /// </summary>
    [JsonPropertyName("itemId")]
    public string ItemId { get; init; } = string.Empty;

    /// <summary>
    /// Gets what the kernel did: <c>queued_followup</c>, <c>steer_applied</c>, or
    /// one of the refusals a queue reports in place.
    /// </summary>
    [JsonPropertyName("disposition")]
    public string Disposition { get; init; } = string.Empty;

    /// <summary>
    /// Gets where in the queue the entry was placed.
    /// </summary>
    [JsonPropertyName("position")]
    public int Position
    {
        get; init;
    }

    /// <summary>
    /// Gets a value indicating whether dispatch is held, so the entry waits.
    /// </summary>
    [JsonPropertyName("paused")]
    public bool Paused
    {
        get; init;
    }
}
