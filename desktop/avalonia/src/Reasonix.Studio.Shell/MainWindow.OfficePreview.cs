using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Kernel.Session;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Workbench;
using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using FileEntry = Reasonix.Studio.Shell.Workbench.WorkspaceFileEntry;

namespace Reasonix.Studio.Shell;

public sealed partial class MainWindow
{
    /// <summary>Gets the selected text or Office document for pane restoration.</summary>
    private string? SelectedWorkbenchDocument => _openFile?.Original.Path
        ?? (RenderedFile.IsVisible
            ? RenderedFile.Content switch
            {
                OfficeDocumentPreview office => office.Path,
                PdfDocumentPreview pdf => pdf.Path,
                _ => null,
            }
            : null);

    /// <summary>Adds the device-local preview preference to the display settings.</summary>
    /// <param name="card">The files and previews card.</param>
    private void AddOfficePreviewSetting(StackPanel card)
    {
        var option = new ToggleSwitch { IsChecked = _workspacePreferences.PreviewOfficeDocuments };
        AutomationProperties.SetAutomationId(option, "action:appearance.office-preview");
        AddRow(card, "common.office_document_preview", option, MessageCatalog.Get("settings.preview_office_in_workbench", Language));
        option.IsCheckedChanged += async (_, _) =>
        {
            _workspacePreferences = _workspacePreferences with
            {
                PreviewOfficeDocuments = option.IsChecked == true,
            };
            if (_connectOnOpen)
            {
                _workspacePreferences.Save();
            }

            if (RenderedFile.IsVisible && RenderedFile.Content is OfficeDocumentPreview preview)
            {
                await OpenEntryAsync(new FileEntry(preview.Path, false)).ConfigureAwait(true);
            }
        };
    }

    /// <summary>Mounts the file presentation before reading its binary content.</summary>
    /// <param name="engine">The runtime that owns this file.</param><param name="entry">The selected workspace entry.</param>
    /// <param name="info">The host preview metadata.</param><param name="ticket">The selection that owns the request.</param>
    /// <returns>The current document read.</returns>
    private async Task ShowOfficePreviewAsync(SessionEngine engine, FileEntry entry, JsonObject info, long ticket)
    {
        var size = (long?)info["size"] ?? 0;
        var limit = (long?)info["limit"] ?? 0;
        var enabled = _workspacePreferences.PreviewOfficeDocuments;
        var preview = new OfficeDocumentPreview(
            entry.Path,
            size,
            limit,
            enabled,
            Language,
            () => OpenEntryAsync(entry),
            engine.IsRemote ? null : () => OpenExternalDocumentAsync(engine, entry),
            OpenOfficeSettingAsync);
        var version = (string?)info["version"] ?? string.Empty;
        _workbench.Buffers[(WorkspaceBufferScope, entry.Path)] = new FileBuffer(new WorkspaceFile(entry.Path, string.Empty, version, ReadOnly: true), "office");
        _openFile = null;
        SetReadOnlyBadge(null);
        ShowPreview(preview);
        SelectWorkspaceFile(entry.Path);
        FileModes.IsVisible = true;
        EditFile.IsVisible = DiffFile.IsVisible = false;
        PreviewFile.Content = MessageCatalog.Get(MessageKey.Common.Preview, Language);
        PreviewFile.IsVisible = true;
        SelectFileMode(PreviewFile);
        SaveFile.IsVisible = false;
        SetFileBarTitle(entry.Path, path: true);
        RebuildTabs();
        if (!enabled || size > limit)
        {
            return;
        }

        var data = await engine.Pages.PreviewContentAsync(entry.Path, version, _closing.Token).ConfigureAwait(true);
        if (ticket != _fileRead || _closing.IsCancellationRequested)
        {
            return;
        }

        if (data is null)
        {
            preview.ShowFailure();
        }
        else
        {
            preview.Show(data);
        }
    }

    /// <summary>Asks the owning desktop host to open this exact workspace file.</summary>
    /// <param name="engine">The runtime that owns this file.</param><param name="entry">The selected workspace entry.</param>
    /// <returns>The launch acknowledgement.</returns>
    private async Task OpenExternalDocumentAsync(SessionEngine engine, FileEntry entry)
    {
        try
        {
            await engine.Pages.OpenDocumentAsync(entry.Path, _closing.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            ReportWorkbenchError(error);
        }
    }

    /// <summary>Returns the reader to the setting that enables this preview.</summary>
    /// <returns>The navigation and focus change.</returns>
    private async Task OpenOfficeSettingAsync()
    {
        OpenPreferences();
        await ShowSectionAsync("display").ConfigureAwait(true);
        SelectSettingsNav("display");
        var row = SettingsBody.GetVisualDescendants().OfType<Control>().FirstOrDefault(item => Equals(item.Tag, "common.office_document_preview"));
        if (row is not null)
        {
            FocusSetting(row);
        }
    }
}
