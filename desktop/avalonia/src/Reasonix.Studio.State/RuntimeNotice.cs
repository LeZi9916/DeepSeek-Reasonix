namespace Reasonix.Studio.State;

/// <summary>
/// A notice about the machine running this conversation rather than about the
/// conversation itself.
/// </summary>
/// <remarks>
/// These describe something that is still true — which model was resolved, which
/// server failed to start — so they hold their own place instead of scrolling away
/// in the transcript as if someone had said them.
/// </remarks>
public sealed record RuntimeNotice
{
    /// <summary>
    /// Gets the notice's identity.
    /// </summary>
    public required string Id
    {
        get; init;
    }

    /// <summary>
    /// Gets the level the kernel reported.
    /// </summary>
    public required string Level
    {
        get; init;
    }

    /// <summary>
    /// Gets the stable code a window can say in its own language.
    /// </summary>
    public string? Code
    {
        get; init;
    }

    /// <summary>
    /// Gets the text the kernel wrote for a terminal, and the fallback when
    /// nothing maps the code.
    /// </summary>
    public required string Text
    {
        get; init;
    }

    /// <summary>
    /// Gets the detail the kernel attached.
    /// </summary>
    public string? Detail
    {
        get; init;
    }
}
