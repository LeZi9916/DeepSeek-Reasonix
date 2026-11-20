namespace Reasonix.Studio.State.Transcript;

/// <summary>
/// One thing the kernel told the person, at a level and under a code.
/// </summary>
public sealed record NoticeItem : TranscriptItem
{
    /// <summary>
    /// Gets the level the kernel reported: <c>info</c>, <c>warn</c> or <c>error</c>.
    /// </summary>
    public required string Level
    {
        get; init;
    }

    /// <summary>
    /// Gets the text the kernel wrote.
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Gets the detail the kernel attached, when it attached one.
    /// </summary>
    public string? Detail
    {
        get; init;
    }

    /// <summary>
    /// Gets the stable code a window can say in its own language.
    /// </summary>
    /// <remarks>
    /// Text is what the kernel wrote for a terminal, and the fallback when nothing
    /// maps the code.
    /// </remarks>
    public string? Code
    {
        get; init;
    }

    /// <summary>
    /// Gets how many identical notices folded into this one.
    /// </summary>
    /// <remarks>
    /// A retry that repeats says the same thing each time; three identical lines
    /// push what came before them off the screen and read as three problems.
    /// </remarks>
    public int? Count
    {
        get; init;
    }
}
