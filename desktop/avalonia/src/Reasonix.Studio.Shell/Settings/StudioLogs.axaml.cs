using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Host;
using System;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Settings;

/// <summary>Shows the log directory and abnormal exits reported by the desktop host.</summary>
internal sealed partial class StudioLogs : UserControl
{
    /// <summary>The clipboard caption before a copy.</summary>
    private readonly string _copyLabel;

    /// <summary>The clipboard caption after a successful copy.</summary>
    private readonly string _copiedLabel;

    /// <summary>Initializes a new instance of the <see cref="StudioLogs"/> class.</summary>
    /// <param name="language">The window language.</param>
    /// <param name="reveal">Opens the desktop host's logs folder.</param>
    internal StudioLogs(InterfaceLanguage language, Func<Task<Refusal?>> reveal)
    {
        InitializeComponent();
        Status.Text = MessageCatalog.Get("common.loading", language);
        More.Text = MessageCatalog.Get("data.older_records_are_not_listed", language);
        Reveal.Header = MessageCatalog.Get("feedback.open_logs_folder", language);
        RevealError.Text = MessageCatalog.Get("feedback.the_logs_folder_could_not", language);
        Reveal.Click += async (_, _) => RevealError.IsVisible = await reveal().ConfigureAwait(true) is not null;
        _copyLabel = MessageCatalog.Get("data.copy_the_log_folder", language);
        _copiedLabel = MessageCatalog.Get("common.copied", language);
        AutomationProperties.SetName(Copy, _copyLabel);
        ToolTip.SetTip(Copy, _copyLabel);
        var copyGlyph = CopyGlyph.Data;
        Copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard && Folder.Text is { Length: > 0 } path)
            {
                await clipboard.SetTextAsync(path).ConfigureAwait(true);
                CopyGlyph.Data = Geometry.Parse("M20 6 9 17l-5-5");
                ToolTip.SetTip(Copy, _copiedLabel);
                await Task.Delay(1000).ConfigureAwait(true);
                CopyGlyph.Data = copyGlyph;
                ToolTip.SetTip(Copy, _copyLabel);
            }
        };
    }

    /// <summary>Reads the host and displays the result or its refusal in this group.</summary>
    /// <param name="api">The machine's desktop host.</param><param name="language">The display language.</param>
    /// <param name="describe">Translates host errors.</param><param name="token">Cancels on leaving the settings page.</param>
    /// <returns>The log inspection.</returns>
    internal async Task LoadAsync(StudioPageClient api, InterfaceLanguage language, Func<Exception, string> describe, CancellationToken token)
    {
        try
        {
            var data = await api.StudioLogsAsync(token).ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
            Draw(data, language);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            Status.Text = describe(error);
        }
    }

    /// <summary>Displays the metadata, with the path copied exactly as reported.</summary>
    /// <param name="data">The host's log metadata.</param><param name="language">The display language.</param>
    private void Draw(JsonObject data, InterfaceLanguage language)
    {
        Folder.Text = (string?)data["dir"];
        Copy.IsVisible = !string.IsNullOrEmpty(Folder.Text);
        var items = data["crashes"] as JsonArray ?? [];
        RecordNote.Text = MessageCatalog.Get(items.Count == 0 ? "data.no_abnormal_exit_record_has" : "data.records_left_when_the_kernel", language);
        var culture = CultureInfo.GetCultureInfo(language == InterfaceLanguage.Zh ? "zh-CN" : "en-US");
        Crashes.Children.Clear();
        foreach (var crash in items)
        {
            var name = (string?)crash?["name"] ?? string.Empty;
            var timestamp = (string?)crash?["at"];
            var date = DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
                ? at.ToLocalTime().ToString(language == InterfaceLanguage.Zh ? "yyyy/M/d HH:mm:ss" : "M/d/yyyy, h:mm:ss tt", culture)
                : timestamp;
            var kilobytes = Math.Max(1, Math.Round(((long?)crash?["bytes"] ?? 0) / 1024d, MidpointRounding.AwayFromZero));
            var row = new TextBlock
            {
                [!TextBlock.FontSizeProperty] = new DynamicResourceExtension("StudioType12"),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 19.8,
                Inlines = new InlineCollection
                {
                    new Run { Text = name, [!Run.FontFamilyProperty] = new DynamicResourceExtension("StudioMonoFamily") },
                    new Run { Text = $" · {date} · {kilobytes.ToString(culture)} KB" },
                },
            };
            row.Bind(TextBlock.ForegroundProperty, row.GetResourceObservable("MutedBrush"));
            Crashes.Children.Add(row);
        }

        Crashes.IsVisible = items.Count > 0;
        More.IsVisible = (bool?)data["truncated"] == true;
        Status.IsVisible = false;
        Result.IsVisible = true;
    }
}
