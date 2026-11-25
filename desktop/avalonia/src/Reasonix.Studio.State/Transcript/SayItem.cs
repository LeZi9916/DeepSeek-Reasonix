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
    /// <summary>Materialized answer.</summary>
    private string _text = string.Empty;

    /// <summary>Materialized thought.</summary>
    private string? _reasoning;

    /// <summary>Open answer segments.</summary>
    private LiveText? _answer;

    /// <summary>Open thought segments.</summary>
    private LiveText? _thought;

    /// <summary>Gets a value indicating whether the answer contains a non-whitespace character.</summary>
    public bool HasContent => (_answer?.HasContent ?? !string.IsNullOrWhiteSpace(_text))
        || (_thought?.HasContent ?? !string.IsNullOrWhiteSpace(_reasoning));

    /// <summary>
    /// Gets the answer text.
    /// </summary>
    public string Text
    {
        get
        {
            return _answer?.Value ?? _text;
        }

        init
        {
            _text = value;
            _answer = null;
        }
    }

    /// <summary>
    /// Gets the reasoning text that preceded the answer.
    /// </summary>
    public string? Reasoning
    {
        get
        {
            return _thought?.Value ?? _reasoning;
        }

        init
        {
            _reasoning = value;
            _thought = null;
        }
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
        get;
        init
        {
            field = value;
            if (value)
            {
                _text = Text;
                _reasoning = Reasoning;
                _answer = null;
                _thought = null;
            }
        }
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

    /// <summary>Appends one delta while sharing the older segments with previous snapshots.</summary>
    /// <param name="text">Accepted delta.</param>
    /// <param name="reasoning">Whether it belongs to thought.</param>
    /// <returns>The updated message.</returns>
    internal SayItem Append(string text, bool reasoning)
    {
        var next = this with
        {
        };
        if (reasoning)
        {
            next._thought = LiveText.Append(_thought, _reasoning, text);
        }
        else
        {
            next._answer = LiveText.Append(_answer, _text, text);
        }

        return next;
    }
}
