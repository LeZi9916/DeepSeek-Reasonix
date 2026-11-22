namespace Reasonix.Studio.Pages.Rail;

/// <summary>One reducer's presentation and actual turn lifetime, kept as one state.</summary>
/// <param name="Run">Running, halt or settled.</param><param name="Live">Whether the turn remains active.</param>
internal sealed record RailActivity(string Run, bool Live);
