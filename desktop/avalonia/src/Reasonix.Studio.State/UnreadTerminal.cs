namespace Reasonix.Studio.State;

/// <summary>
/// A turn the transcript read back from the record, with no frame saying how it
/// ended, so neither a delivery nor a stop can be claimed.
/// </summary>
public sealed record UnreadTerminal : TurnTerminal;
