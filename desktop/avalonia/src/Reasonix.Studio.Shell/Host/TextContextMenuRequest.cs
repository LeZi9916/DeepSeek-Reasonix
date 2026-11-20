namespace Reasonix.Studio.Shell.Host;

/// <summary>
/// What the text context menu may offer, and where it should open.
/// </summary>
/// <param name="Editable">Whether the field under the pointer accepts typing.</param>
/// <param name="Selected">Whether anything is selected.</param>
/// <param name="CanUndo">Whether an undo is available.</param>
/// <param name="CanRedo">Whether a redo is available.</param>
/// <param name="Quote">Whether the shell offers quoting the selection into the composer.</param>
/// <param name="X">Where the menu's left edge goes, in this window's pixels.</param>
/// <param name="Y">Where the menu's top edge goes, in this window's pixels.</param>
public sealed record TextContextMenuRequest(
    bool Editable,
    bool Selected,
    bool CanUndo,
    bool CanRedo,
    bool Quote,
    double X,
    double Y);
