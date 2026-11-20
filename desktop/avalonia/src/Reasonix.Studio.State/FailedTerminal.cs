namespace Reasonix.Studio.State;

/// <summary>
/// A turn that failed.
/// </summary>
/// <param name="Err">What the kernel said went wrong.</param>
public sealed record FailedTerminal(string Err) : TurnTerminal;
