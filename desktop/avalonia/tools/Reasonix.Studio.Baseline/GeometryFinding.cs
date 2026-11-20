namespace Reasonix.Studio.Baseline;

/// <summary>
/// One difference between the baseline and what a shell drew.
/// </summary>
/// <param name="Kind">What kind of value differed: box, style, token, missing or extra.</param>
/// <param name="Identity">The node or token the difference belongs to.</param>
/// <param name="Expected">What the reference frontend had.</param>
/// <param name="Actual">What the replacement shell produced.</param>
public sealed record GeometryFinding(string Kind, string Identity, string Expected, string Actual);
