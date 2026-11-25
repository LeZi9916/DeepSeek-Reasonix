using Reasonix.Studio.Kernel.Client;

namespace Reasonix.Studio.Shell.Workbench;

/// <summary>A text buffer and the revision it was read against.</summary>
/// <param name="file">File as read from the kernel.</param>
/// <param name="kind">Preview type selected by the kernel.</param>
internal sealed class FileBuffer(WorkspaceFile file, string? kind)
{
    /// <summary>Gets the kernel-declared preview type.</summary>
    public string? Kind { get; } = kind;

    /// <summary>Gets or sets the version used for the next save.</summary>
    public WorkspaceFile Original { get; set; } = file;

    /// <summary>Gets or sets the text in the editor.</summary>
    public string Draft { get; set; } = file.Content;

    /// <summary>Gets or sets a value indicating whether a write is in flight.</summary>
    public bool Saving
    {
        get; set;
    }

    /// <summary>Gets a value indicating whether this buffer holds unsaved edits.</summary>
    public bool Dirty => Draft != Original.Content;

    /// <summary>Gets a value indicating whether a write can be requested.</summary>
    public bool CanSave => !Saving && !Original.ReadOnly && Dirty;
}
