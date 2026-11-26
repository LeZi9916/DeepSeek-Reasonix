using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Kernel.Session;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Workbench;
using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using FileEntry = Reasonix.Studio.Shell.Workbench.WorkspaceFileEntry;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Reads and mounts the PDF selected by the reader.</summary>
    /// <param name="engine">The runtime owning this workspace.</param><param name="entry">The selected document.</param>
    /// <param name="info">The host's version and preview limit.</param><param name="ticket">The current selection.</param>
    /// <param name="animateTab">Whether the reader explicitly opened the tab.</param><returns>The pending read.</returns>
    private async Task ShowPdfPreviewAsync(SessionEngine engine, FileEntry entry, JsonObject info, long ticket, bool animateTab)
    {
        var key = (WorkspaceBufferScope, entry.Path);
        var version = (string?)info["version"] ?? string.Empty;
        var file = new WorkspaceFile(entry.Path, string.Empty, version, ReadOnly: true);
        var newlyOpened = !_workbench.Buffers.TryGetValue(key, out var buffer);
        if (buffer is null)
        {
            buffer = new FileBuffer(file, "pdf");
            _workbench.Buffers[key] = buffer;
        }
        else
        {
            buffer.Original = file;
        }

        ReleasePreview();
        _openFile = null;
        SetReadOnlyBadge(null);
        var preview = new PdfDocumentPreview(
            buffer,
            Language,
            () => OpenEntryAsync(entry),
            engine.IsRemote ? null : () => OpenExternalDocumentAsync(engine, entry));
        ShowPreview(preview);
        SelectWorkspaceFile(entry.Path);
        SetFileBarTitle(entry.Path, path: true);
        FileModes.IsVisible = PreviewFile.IsVisible = true;
        EditFile.IsVisible = DiffFile.IsVisible = SaveFile.IsVisible = false;
        PreviewFile.Content = MessageCatalog.Get(MessageKey.Common.Preview, Language);
        SelectFileMode(PreviewFile);
        RebuildTabs(animateTab && newlyOpened ? entry.Path : null, animateSelection: animateTab);

        var size = (long?)info["size"] ?? 0;
        var limit = (long?)info["limit"] ?? 0;
        if (size > limit)
        {
            preview.ShowFailure(MessageCatalog.Get(
                "workbench.size_preview_limit", Language, ("size", Format.Bytes(size, Language)), ("limit", Format.Bytes(limit, Language))));
            return;
        }

        var bytes = await engine.Pages.PreviewContentAsync(entry.Path, version, _closing.Token).ConfigureAwait(true);
        if (ticket != _fileRead || _closing.IsCancellationRequested)
        {
            return;
        }

        if (bytes is null)
        {
            preview.ShowFailure();
        }
        else
        {
            preview.Show(bytes);
        }
    }

    /// <summary>Previews a delimited draft without changing the source editor's buffer.</summary>
    /// <param name="file">The selected workspace file.</param><returns>The background table read.</returns>
    private async Task ShowTableAsync(FileBuffer file)
    {
        var ticket = ++_fileRead;
        var text = file.Draft;
        var divider = Path.GetExtension(file.Original.Path).Equals(".tsv", StringComparison.OrdinalIgnoreCase) ? '\t' : ',';
        var preview = new DelimitedDocumentPreview(file, Language);
        file.ShowsSource = false;
        ShowPreview(preview);
        FileModes.IsVisible = true;
        DrawFileToolbar(file);
        SelectFileMode(PreviewFile);
        try
        {
            var rows = await Task.Run(() => DelimitedTable.Parse(text, divider), _closing.Token).ConfigureAwait(true);
            if (ticket == _fileRead && !_closing.IsCancellationRequested)
            {
                preview.Show(rows);
            }
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
        }
        catch (FormatException)
        {
            if (ticket == _fileRead && !_closing.IsCancellationRequested)
            {
                preview.ShowFailure(Language);
            }
        }
    }
}
