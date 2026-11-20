namespace Reasonix.Studio.Shell.Host;

/// <summary>
/// A kernel refusal as the shell hands it back: the code, and an English fallback.
/// </summary>
/// <remarks>
/// The code is the identity and the error is only its wording; a caller that
/// matches on the wording is one edit away from never matching again.
/// </remarks>
/// <param name="Code">The kernel's dotted refusal code, when it named one.</param>
/// <param name="Error">The kernel's wording, for a person rather than a decision.</param>
public sealed record Refusal(string? Code, string? Error);
