using Avalonia;
using Avalonia.Media;
using System.Linq;

namespace Reasonix.Studio.Shell.Workbench;

/// <summary>A workspace path projected into the visible explorer tree.</summary>
/// <param name="Path">Workspace-relative identity.</param>
/// <param name="Directory">Whether the path is a folder.</param>
public sealed record WorkspaceFileEntry(string Path, bool Directory)
{
    /// <summary>Gets the displayed basename.</summary>
    public string Name => Path.Split('/').Last();

    /// <summary>Gets the row indentation from the path hierarchy.</summary>
    public Thickness Indent => new(8 + (14 * Path.Count(c => c == '/')), 0, 10, 0);

    /// <summary>Gets a value indicating whether this folder is expanded.</summary>
    public bool Expanded
    {
        get; init;
    }

    /// <summary>Gets the Git status supplied by the owning repository.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Gets the folder disclosure geometry.</summary>
    public Geometry Chevron => Geometry.Parse(Expanded ? "M6 9l6 6 6-6" : "m9 6 6 6-6 6");

    /// <summary>Gets the canonical explorer action.</summary>
    public string Action => Directory ? "action:workbench.folder" : "action:workbench.file";

    /// <inheritdoc/>
    public override string ToString() => Name;
}
