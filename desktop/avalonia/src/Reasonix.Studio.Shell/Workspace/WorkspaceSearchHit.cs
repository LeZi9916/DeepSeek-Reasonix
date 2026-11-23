using Avalonia.Media;
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

    /// <summary>Gets the results row height, with a smaller section heading.</summary>
    public double RowHeight => Selectable ? 36 : 24;

    /// <summary>Gets the same destination symbol used by the palette for this row.</summary>
    public Geometry? Icon => Selectable ? Geometry.Parse(IconPath) : null;

    /// <summary>Gets the stable key used to remember this operation across openings.</summary>
    public string CanonicalKey => Identity.Length > 0 ? Identity : System.Text.Json.JsonSerializer.Serialize(new[] { Kind, Label, Detail });

    /// <summary>Gets the selected destination's outline.</summary>
    private string IconPath => (Identity, Kind) switch
    {
        ("c:common.start_a_new_session", _) => "M5 12h14 M12 5v14",
        ("c:common.find_in_this_conversation", _) => "M18 10 A8 8 0 1 1 2 10 A8 8 0 1 1 18 10 M16 16l6 6",
        ("c:session.show_conversation", _) => "M4 6h16 M4 12h16 M4 18h16",
        ("c:session.show_run_analysis", _) => "M3 6h18 M3 18h18 M3 12h18 M6 6h.01 M9 12h.01 M15 18h.01",
        ("c:session.show_workbench", _) => "M4 3h16a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2Z M9 3v18",
        (_, "sessions") => "M21 5H3v13l4-3h14Z",
        (_, "settings") => "M5 7h14 M5 12h14 M5 17h14 M8 5v4 M16 10v4 M11 15v4",
        _ => "M6 12h12 M12 6v12",
    };
}
