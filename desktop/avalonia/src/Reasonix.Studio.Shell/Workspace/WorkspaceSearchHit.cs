using System;

namespace Reasonix.Studio.Shell.Workspace;

/// <summary>One live action, canonical session or setting in global search.</summary>
/// <param name="Kind">Actions, sessions or settings.</param><param name="Label">Localized title.</param>
/// <param name="Detail">Project, host or setting path.</param><param name="Run">The canonical operation.</param>
/// <param name="Keywords">Search aliases which do not become displayed identities.</param><param name="Identity">Stable canonical recent-use key.</param>
public sealed record WorkspaceSearchHit(string Kind, string Label, string Detail, Action Run, string Keywords = "", string Identity = "")
{
    /// <summary>Gets a value indicating whether this row dispatches an action.</summary>
    public bool Selectable => Kind != "heading";

    /// <summary>Gets the reference row's title size.</summary>
    public double TextSize => Selectable ? 13 : 11;

    /// <summary>Gets the stable key used to remember this operation across openings.</summary>
    public string CanonicalKey => Identity.Length > 0 ? Identity : System.Text.Json.JsonSerializer.Serialize(new[] { Kind, Label, Detail });
}
