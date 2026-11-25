using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Workbench;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Creates a breadcrumb run that follows the active theme.</summary>
    /// <param name="text">Path segment text.</param><param name="brush">Theme resource.</param>
    /// <param name="weight">Segment emphasis.</param><returns>The visible segment.</returns>
    private static Run PathRun(string text, string brush, FontWeight weight) =>
        new()
        {
            Text = text,
            FontWeight = weight,
            [!Run.ForegroundProperty] = new DynamicResourceExtension(brush),
        };

    /// <summary>Marks the active presentation of the file in the toolbar.</summary>
    /// <param name="active">Selected mode.</param>
    private void SelectFileMode(Button active)
    {
        foreach (var mode in new[] { PreviewFile, EditFile, DiffFile })
        {
            mode.Classes.Set("active", mode == active);
        }
    }

    /// <summary>Draws a path as muted directories and a bold filename.</summary>
    /// <param name="text">Path or plain title.</param>
    /// <param name="path">Whether the text is a workspace-relative file path.</param>
    private void SetFileBarTitle(string text, bool path)
    {
        WorkbenchTitle.Text = text;
        WorkbenchTitle.IsVisible = !path;
        Breadcrumb.IsVisible = path;
        if (!path)
        {
            return;
        }

        ToolTip.SetTip(Breadcrumb, text);
        Breadcrumb.Inlines?.Clear();
        var parts = text.Split('/');
        for (var i = 0; i < parts.Length - 1; i++)
        {
            Breadcrumb.Inlines?.Add(PathRun(parts[i] + " ", "FaintBrush", FontWeight.Normal));
            Breadcrumb.Inlines?.Add(PathRun("/ ", "GhostBrush", FontWeight.Normal));
        }

        Breadcrumb.Inlines?.Add(PathRun(parts[^1], "TextBrush", FontWeight.Medium));
    }

    /// <summary>Includes the granted folder when the file resides outside the workspace.</summary>
    /// <param name="file">File identity supplied by the host.</param><param name="path">The selected file.</param>
    private void SetFileBarTitle(WorkspaceFile file, string path) =>
        SetFileBarTitle(file.ReadOnly && file.Root is { Length: > 0 } root ? root.TrimEnd('/', '\\') + "/" + path : path, path: true);

    /// <summary>Shows the views and actions available for the open file.</summary>
    /// <param name="file">The current buffer and host file grant.</param>
    private void DrawFileToolbar(FileBuffer file)
    {
        var renderable = file.Kind is "markdown" or "html";
        PreviewFile.IsVisible = renderable;
        PreviewFile.Content = MessageCatalog.Get(file.Kind == "markdown" ? MessageKey.Workbench.Read : MessageKey.Common.Preview, Language);
        EditFile.Content = MessageCatalog.Get(!file.Original.ReadOnly && renderable ? MessageKey.Common.Edit : MessageKey.Common.Files, Language);
        DiffFile.IsVisible = !file.Original.ReadOnly;
        SaveFile.IsVisible = !file.Original.ReadOnly;
        SaveFile.IsEnabled = file.CanSave;
        FileMore.IsVisible = !file.Original.ReadOnly;
    }

    /// <summary>Shows why a granted file cannot be saved.</summary>
    /// <param name="file">Current buffer, or null when no file is open.</param>
    private void SetReadOnlyBadge(FileBuffer? file)
    {
        var readOnly = file?.Original.ReadOnly == true;
        ReadOnlyBadge.IsVisible = readOnly;
        ReadOnlyBadgeText.Text = readOnly ? MessageCatalog.Get("workbench.read_only_from_a_folder", Language) : string.Empty;
        ToolTip.SetTip(ReadOnlyBadge, readOnly ? file?.Original.Root ?? string.Empty : string.Empty);
    }
}
