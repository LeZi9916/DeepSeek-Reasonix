namespace Reasonix.Studio.Shell.Composer;

/// <summary>One local branch in the composer palette.</summary>
/// <param name="Name">Branch name.</param>
/// <param name="Current">Whether the workspace is on it.</param>
/// <param name="Worktree">Worktree holding it, or an empty string.</param>
/// <param name="Remote">Whether the row is a remote-tracking branch.</param>
public sealed record BranchChoice(string Name, bool Current, string Worktree, bool Remote = false);
