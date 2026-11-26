using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
using AvaloniaEdit.TextMate;
using System;
using System.IO;
using System.Linq;
using TextMateSharp.Grammars;

namespace Reasonix.Studio.Shell.Workbench;

/// <summary>Edits native, retained workspace documents and exposes their reading position.</summary>
public sealed class WorkbenchFileEditor : TextEditor
{
    /// <summary>The grammar catalog used by this window's editor.</summary>
    private static readonly Lazy<RegistryOptions> s_grammar = new(() => new RegistryOptions(ThemeName.LightPlus));

    /// <summary>Coalesces folding scans while typing.</summary>
    private readonly DispatcherTimer _foldUpdates = new() { Interval = TimeSpan.FromMilliseconds(150) };

    /// <summary>Tracks the current native folding markers.</summary>
    private FoldingManager? _folds;

    /// <summary>Colors the file with its TextMate grammar.</summary>
    private TextMate.Installation? _syntax;

    /// <summary>The file shown in this editor, including when the preview obscures it.</summary>
    private FileBuffer? _file;

    /// <summary>Initializes a new instance of the <see cref="WorkbenchFileEditor"/> class.</summary>
    public WorkbenchFileEditor()
    {
        Options.HighlightCurrentLine = true;
        Options.EnableHyperlinks = false;
        Options.EnableEmailHyperlinks = false;
        Options.AllowScrollBelowDocument = true;
        Options.LineHeightFactor = 1.35;
        TextChanged += (_, _) =>
        {
            if (_folds is not null)
            {
                _foldUpdates.Stop();
                _foldUpdates.Start();
            }
        };
        _foldUpdates.Tick += (_, _) =>
        {
            _foldUpdates.Stop();
            RebuildFolds();
        };
        ActualThemeVariantChanged += (_, _) => ApplySyntaxTheme();
        DetachedFromVisualTree += (_, _) => _foldUpdates.Stop();
    }

    /// <summary>Gets the one-based line containing the caret.</summary>
    public int CurrentLine => TextArea.Caret.Line;

    /// <inheritdoc/>
    protected override Type StyleKeyOverride => typeof(TextEditor);

    /// <summary>Mounts a retained text document and its navigation state.</summary>
    /// <param name="file">The newly selected file buffer.</param>
    internal void Open(FileBuffer file)
    {
        if (ReferenceEquals(_file, file))
        {
            IsReadOnly = file.Original.ReadOnly;
            return;
        }

        Capture();
        ResetTools();
        _file = file;
        Document = file.Document;
        IsReadOnly = file.Original.ReadOnly;
        var caret = Math.Clamp(file.EditorPosition.Caret, 0, Document.TextLength);
        var selection = Math.Clamp(file.EditorPosition.SelectionStart, 0, Document.TextLength);
        Select(selection, Math.Clamp(file.EditorPosition.SelectionLength, 0, Document.TextLength - selection));
        CaretOffset = caret;

        var extension = Path.GetExtension(file.Original.Path).ToLowerInvariant();
        if (s_grammar.Value.GetScopeByExtension(extension) is { Length: > 0 } scope)
        {
            _syntax = this.InstallTextMate(s_grammar.Value, false);
            _syntax.SetGrammar(scope);
            ApplySyntaxTheme();
        }

        if (WorkbenchCodeFolds.Supports(extension))
        {
            _folds = FoldingManager.Install(TextArea);
            RebuildFolds();
            foreach (var fold in _folds.AllFoldings)
            {
                fold.IsFolded = file.FoldedOffsets.Contains(fold.StartOffset);
            }
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                if (ReferenceEquals(_file, file))
                {
                    ScrollToHorizontalOffset(file.EditorPosition.Offset.X);
                    ScrollToVerticalOffset(file.EditorPosition.Offset.Y);
                }
            },
            DispatcherPriority.Loaded);
    }

    /// <summary>Remembers selection, scroll and folds while the file stays open.</summary>
    internal void Capture()
    {
        if (_file is { } file && ReferenceEquals(Document, file.Document))
        {
            file.EditorPosition = (CaretOffset, SelectionStart, SelectionLength, new Vector(HorizontalOffset, VerticalOffset));
            file.FoldedOffsets = _folds?.AllFoldings.Where(fold => fold.IsFolded).Select(fold => fold.StartOffset).ToArray() ?? [];
        }
    }

    /// <summary>Detaches the current document without modifying retained content.</summary>
    internal void ClearBuffer()
    {
        Capture();
        ResetTools();
        _file = null;
        Document = new TextDocument();
    }

    /// <summary>Moves the caret to a file line and focuses the native editing surface.</summary>
    /// <param name="line">A one-based line number.</param>
    internal void GoToLine(int line)
    {
        var location = Document.GetLineByNumber(Math.Clamp(line, 1, Document.LineCount));
        foreach (var fold in _folds?.GetFoldingsContaining(location.Offset) ?? [])
        {
            fold.IsFolded = false;
        }

        Select(location.Offset, 0);
        CaretOffset = location.Offset;
        ScrollToLine(location.LineNumber);
        TextArea.Focus();
    }

    /// <summary>Switches the grammar color palette with the window.</summary>
    private void ApplySyntaxTheme() => _syntax?.SetTheme(s_grammar.Value.LoadTheme(
        ActualThemeVariant == ThemeVariant.Dark ? ThemeName.DarkPlus : ThemeName.LightPlus));

    /// <summary>Updates fold markers for the active document text.</summary>
    private void RebuildFolds()
    {
        if (_file is { } file && _folds is not null)
        {
            _folds.UpdateFoldings(WorkbenchCodeFolds.Create(Document, Path.GetExtension(file.Original.Path).ToLowerInvariant()), -1);
        }
    }

    /// <summary>Releases file-specific editor state before replacing the document.</summary>
    private void ResetTools()
    {
        _foldUpdates.Stop();
        SearchPanel?.Close();
        _syntax?.Dispose();
        _syntax = null;
        if (_folds is { } folds)
        {
            FoldingManager.Uninstall(folds);
            _folds = null;
        }
    }
}
