namespace Reasonix.Studio.State.Transcript;

/// <summary>
/// What a <c>remember</c> call wrote, read off its own arguments.
/// </summary>
/// <remarks>
/// Saving a fact changes what the agent does in later sessions, which no other
/// tool call does, so it gets a card of its own rather than scrolling past as one
/// more step. The card reads the call's arguments because they carry what was
/// saved and how it will be recalled; the tool's textual receipt does not.
/// </remarks>
public sealed record RememberedFact
{
    /// <summary>
    /// Gets the name the fact is filed under.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the title shown on the card, falling back to the name.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Gets what the fact says.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Gets the scope the fact was saved at: <c>project</c> or <c>global</c>.
    /// </summary>
    public string Scope { get; init; } = "project";

    /// <summary>
    /// Gets how the fact will be recalled: <c>relevant</c> or <c>always</c>.
    /// </summary>
    public string Activation { get; init; } = "relevant";

    /// <summary>
    /// Gets the full text the fact was saved with.
    /// </summary>
    public string Body { get; init; } = string.Empty;
}
