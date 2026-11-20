namespace Reasonix.Studio.State;

/// <summary>
/// A turn that ended without delivering what it owed.
/// </summary>
/// <param name="Outcome">The kernel's own word for why.</param>
public sealed record IncompleteTerminal(string Outcome) : TurnTerminal;
