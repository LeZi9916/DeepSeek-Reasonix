using Avalonia.Controls;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Workbench;

/// <summary>The file metadata, commands and reading surface for a workspace Office document.</summary>
internal sealed partial class OfficeDocumentPreview : UserControl
{
    /// <summary>The preview's display language.</summary>
    private readonly InterfaceLanguage _language;

    /// <summary>Initializes a new instance of the <see cref="OfficeDocumentPreview"/> class.</summary>
    /// <param name="path">Workspace-relative file path.</param><param name="size">File size from the host.</param>
    /// <param name="limit">The host preview limit.</param><param name="enabled">Whether Office preview is enabled.</param>
    /// <param name="language">The window language.</param><param name="refresh">Reads the selected file again.</param>
    /// <param name="open">Asks the host to open the file externally, when available.</param>
    /// <param name="settings">Opens the setting that controls preview.</param>
    internal OfficeDocumentPreview(string path, long size, long limit, bool enabled, InterfaceLanguage language, Func<Task> refresh, Func<Task>? open, Func<Task> settings)
    {
        InitializeComponent();
        _language = language;
        Path = path;
        SizeCaption.Text = size.ToString("N0", language == InterfaceLanguage.Zh ? CultureInfo.GetCultureInfo("zh-CN") : CultureInfo.GetCultureInfo("en-US")) + " B";
        FileName.Text = System.IO.Path.GetFileName(path);
        DisabledText.Text = " · " + Text("chat.office_preview_is_off");
        OpenInApp.Content = OpenDisabled.Content = Text("chat.open_in_system_application");
        GoToSetting.Content = Text("chat.turn_preview_on_in_settings");
        Refresh.Content = Text("common.re_freeze");
        OpenDisabled.IsVisible = open is not null;
        OpenInApp.IsVisible = enabled && open is not null;
        if (open is not null)
        {
            OpenDisabled.Click += async (_, _) => await open().ConfigureAwait(true);
            OpenInApp.Click += async (_, _) => await open().ConfigureAwait(true);
        }

        Refresh.Click += async (_, _) => await refresh().ConfigureAwait(true);
        GoToSetting.Click += async (_, _) => await settings().ConfigureAwait(true);
        DisabledCard.IsVisible = !enabled;
        Notice.IsVisible = enabled;
        if (enabled)
        {
            Notice.Text = size > limit
                ? Text("workbench.file_too_large_to_preview") + "\n" + MessageCatalog.Get(
                    "workbench.size_preview_limit",
                    language,
                    ("size", Format.Bytes(size, language)),
                    ("limit", Format.Bytes(limit, language)))
                : Text("common.loading");
        }
    }

    /// <summary>Gets the file shown by this workbench surface.</summary>
    internal string Path
    {
        get;
    }

    /// <summary>Loads a version-checked document into the isolated renderer.</summary>
    /// <param name="bytes">The document read from the workspace.</param>
    internal void Show(byte[] bytes)
    {
        var guest = new OfficeWebView(Path, bytes, _language, ShowFailure);
        Document.Content = guest;
        Notice.IsVisible = false;
    }

    /// <summary>Reports a failed or expired preview within the document surface.</summary>
    internal void ShowFailure()
    {
        Document.Content = null;
        Notice.Text = Text("chat.this_file_could_not_be");
        Notice.IsVisible = true;
    }

    /// <summary>Translates a label drawn by the workbench.</summary>
    /// <param name="key">The message identity.</param><returns>The localized label.</returns>
    private string Text(string key) => MessageCatalog.Get(key, _language);
}
