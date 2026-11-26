using Avalonia;
using AvaloniaEdit.Document;
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

    /// <summary>Gets the retained native document and undo history.</summary>
    public TextDocument Document { get; } = new(file.Content);

    /// <summary>Gets or sets the text in the editor.</summary>
    public string Draft
    {
        get
        {
            return Document.Text;
        }

        set
        {
            if (Document.Text != value)
            {
                Document.Text = value;
            }
        }
    }

    /// <summary>Gets or sets the selection and scroll position of the native editor.</summary>
    public (int Caret, int SelectionStart, int SelectionLength, Vector Offset) EditorPosition
    {
        get; set;
    }

    /// <summary>Gets or sets the collapsed ranges in this file.</summary>
    public int[] FoldedOffsets
    {
        get; set;
    } = [];

    /// <summary>Gets or sets the retained PDF page and zoom.</summary>
    public (int Page, double Zoom) PdfPosition
    {
        get; set;
    } = (1, 0);

    /// <summary>Gets or sets the scroll position of a delimited table.</summary>
    public Vector TableOffset
    {
        get; set;
    }

    /// <summary>Gets or sets the last view selected for the buffer.</summary>
    public bool? ShowsSource
    {
        get; set;
    }

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
