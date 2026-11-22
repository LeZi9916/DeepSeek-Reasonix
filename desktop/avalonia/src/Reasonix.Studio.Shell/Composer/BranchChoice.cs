namespace Reasonix.Studio.Shell.Composer;

/// <summary>One local branch in the composer palette.</summary>
/// <param name="Name">Branch name.</param>
/// <param name="Current">Whether the workspace is on it.</param>
/// <param name="Worktree">Worktree holding it, or an empty string.</param>
public sealed record BranchChoice(string Name, bool Current, string Worktree);
