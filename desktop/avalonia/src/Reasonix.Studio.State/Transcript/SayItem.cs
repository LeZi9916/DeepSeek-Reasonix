namespace Reasonix.Studio.State.Transcript;

/// <summary>
/// How a thought ended early.
/// </summary>
public enum ThoughtCut
{
    /// <summary>
    /// The turn was stopped before the thought finished.
    /// </summary>
    Stopped,

    /// <summary>
    /// The turn failed before the thought finished.
    /// </summary>
    Failed,
}

/// <summary>
/// One model message, whether it is still arriving or settled.
/// </summary>
public sealed record SayItem : TranscriptItem
{
    /// <summary>
    /// Gets the answer text.
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Gets the reasoning text that preceded the answer.
    /// </summary>
    public string? Reasoning
    {
        get; init;
    }

    /// <summary>
    /// Gets a value indicating whether the message is complete.
    /// </summary>
    /// <remarks>
    /// An unsealed card keeps a caret blinking and its reveal clock running on
    /// text nothing will add to, which is why a turn that ends seals every open
    /// one.
    /// </remarks>
    public bool Done
    {
        get; init;
    }

    /// <summary>Gets when this runtime observed the first reasoning delta, in Unix milliseconds.</summary>
    public long? ThoughtStartedAt
    {
        get; init;
    }

    /// <summary>
    /// Gets how long the model thought, once that is known.
    /// </summary>
    public long? ThoughtMs
    {
        get; init;
    }

    /// <summary>
    /// Gets which model wrote this when it was not the turn's own.
    /// </summary>
    public string? Source
    {
        get; init;
    }

    /// <summary>
    /// Gets why the thought stopped arriving.
    /// </summary>
    public ThoughtCut? Cut
    {
        get; init;
    }
}
