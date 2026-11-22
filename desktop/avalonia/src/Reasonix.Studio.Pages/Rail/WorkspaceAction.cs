namespace Reasonix.Studio.Pages.Rail;

/// <summary>One canonical project or conversation operation chosen from a native menu.</summary>
/// <param name="Action">Stable presentation action identity.</param>
/// <param name="Target">Machine, project and conversation identity.</param>
/// <param name="Session">Canonical row metadata.</param><param name="Value">Selected catalog value.</param>
public sealed record WorkspaceAction(string Action, SessionOpenTarget Target, Reasonix.Studio.Contracts.Tree.TreeSession? Session = null, string Value = "");
