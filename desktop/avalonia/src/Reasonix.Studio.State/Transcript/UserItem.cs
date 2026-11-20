namespace Reasonix.Studio.State.Transcript;

/// <summary>
/// One message a person sent.
/// </summary>
public sealed record UserItem : TranscriptItem
{
    /// <summary>
    /// Gets the message's text.
    /// </summary>
    public required string Text
    {
        get; init;
    }

    /// <summary>
    /// Gets a value indicating whether the kernel has not yet named a turn for it.
    /// </summary>
    public bool Pending
    {
        get; init;
    }

    /// <summary>
    /// Gets a value indicating whether this was said into a turn already running.
    /// </summary>
    /// <remarks>
    /// Guidance never was a turn of its own, so it has no checkpoint and nothing
    /// rewinds to it.
    /// </remarks>
    public bool Steer
    {
        get; init;
    }

    /// <summary>
    /// Gets the durable queue entry this row stands for while it waits.
    /// </summary>
    public string? ItemId
    {
        get; init;
    }

    /// <summary>
    /// Gets which wait the row is in: <c>steer</c> lands at the next tool boundary,
    /// <c>followup</c> when the running turn is done.
    /// </summary>
    public string? Queued
    {
        get; init;
    }

    /// <summary>
    /// Gets the turn number the kernel named for this message.
    /// </summary>
    public int? AuthoredTurn
    {
        get; init;
    }

    /// <summary>
    /// Gets the session-log index the kernel named for this message.
    /// </summary>
    public int? MsgIndex
    {
        get; init;
    }

    /// <summary>
    /// Gets when the message landed in the conversation, in unix milliseconds.
    /// </summary>
    public long? At
    {
        get; init;
    }
}
