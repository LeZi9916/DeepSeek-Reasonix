namespace Reasonix.Studio.Kernel.Client;

/// <summary>One message from the native PTY stream.</summary>
/// <param name="Kind">Replay, output or exit.</param>
/// <param name="Data">Base64 terminal bytes.</param>
/// <param name="Code">Exit code of a completed shell.</param>
public sealed record TerminalFrame(string Kind, string? Data = null, int? Code = null);
