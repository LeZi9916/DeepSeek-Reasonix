namespace Reasonix.Studio.Pages;

/// <summary>
/// One answer to an approval the kernel asked for.
/// </summary>
/// <param name="Id">The approval the kernel named.</param>
/// <param name="Allow">Whether the call may run.</param>
/// <param name="Session">Whether the same answer covers the rest of this session.</param>
/// <param name="Persist">Whether the answer is remembered beyond this session.</param>
public sealed record ApprovalVerdict(string Id, bool Allow, bool Session, bool Persist);
