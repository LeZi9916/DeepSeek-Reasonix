namespace Reasonix.Studio.State;

/// <summary>The current response wait and the kernel's optional retry envelope.</summary>
/// <param name="Since">The Unix-millisecond start of this attempt or backoff.</param>
/// <param name="IsRetry">Whether the kernel reported a retry.</param>
/// <param name="Attempt">The reported attempt number.</param>
/// <param name="Max">The maximum attempts, or zero for unlimited.</param>
/// <param name="Scope">The request or stream scope of a retry.</param>
/// <param name="Cause">The reported failure cause.</param>
/// <param name="Status">The reported HTTP status.</param>
/// <param name="DelayMs">The backoff before the next attempt.</param>
/// <param name="TimeoutSecs">The next attempt's response timeout.</param>
public sealed record ModelResponseWait(long Since, bool IsRetry = false, int Attempt = 0, int Max = 0, string? Scope = null, string? Cause = null, int Status = 0, long DelayMs = 0, int TimeoutSecs = 0);
