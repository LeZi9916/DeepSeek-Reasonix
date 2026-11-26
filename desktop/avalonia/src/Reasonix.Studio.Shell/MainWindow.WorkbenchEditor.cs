using Avalonia.Input;
using Reasonix.Studio.Pages.I18n;
using System.Globalization;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Connects the workbench's native file navigation and selection actions.</summary>
    private void InitializeEditorNavigation()
    {
        Label(FileGotoLine, "workbench.go_to_line");
        GoToFileLine.Content = MessageCatalog.Get("workbench.go", Language);
        QuoteFileSelection.Content = MessageCatalog.Get("workbench.add_to_conversation", Language);
        GoToFileLine.Click += (_, _) => GoToLine();
        FileGotoLine.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                GoToLine();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CloseEditorGoto(restoreFocus: true);
                e.Handled = true;
            }
        };
        FileEditor.TextArea.SelectionChanged += (_, _) => QuoteFileSelection.IsVisible = FileEditor.SelectionLength > 0;
        QuoteFileSelection.Click += (_, _) =>
        {
            if (_openFile is { } file && FileEditor.SelectionLength > 0)
            {
                AddQuoteChip(FileEditor.SelectedText, file.Original.Path);
            }
        };
    }

    /// <summary>Handles file find and go-to-line while an editor owns keyboard focus.</summary>
    /// <param name="key">The primary-modifier key gesture.</param><returns>Whether the workbench handled it.</returns>
    private bool NavigateEditor(Key key)
    {
        if (!FileEditor.IsVisible || !FileEditor.IsKeyboardFocusWithin)
        {
            return false;
        }

        if (key == Key.F && FileEditor.SearchPanel is { } search)
        {
            search.Open();
            search.Reactivate();
            return true;
        }

        if (key == Key.G)
        {
            FileGoto.IsVisible = true;
            FileGotoLine.Text = FileEditor.CurrentLine.ToString(CultureInfo.CurrentCulture);
            FileGotoLine.Focus();
            FileGotoLine.SelectAll();
            return true;
        }

        return false;
    }

    /// <summary>Moves to the supplied line while keeping invalid input available to correct.</summary>
    private void GoToLine()
    {
        if (int.TryParse(FileGotoLine.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var line))
        {
            CloseEditorGoto(restoreFocus: false);
            FileEditor.GoToLine(line);
        }
    }

    /// <summary>Dismisses the transient file navigation field.</summary>
    /// <param name="restoreFocus">Whether a reader dismissal should focus the editor.</param>
    private void CloseEditorGoto(bool restoreFocus)
    {
        FileGoto.IsVisible = false;
        if (restoreFocus)
        {
            FileEditor.TextArea.Focus();
        }
    }
}
