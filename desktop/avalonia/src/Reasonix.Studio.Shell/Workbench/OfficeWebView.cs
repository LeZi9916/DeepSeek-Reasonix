using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Browser;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;

namespace Reasonix.Studio.Shell.Workbench;

/// <summary>Hosts the bundled Office renderer without granting it workspace or network access.</summary>
internal sealed class OfficeWebView : Border
{
    /// <summary>The bundled document, shared by workspace previews in this process.</summary>
    private static readonly Lazy<string> s_document = new(ReadDocument);

    /// <summary>The localized strings read by the shared Office preview.</summary>
    private static readonly string[] s_labels =
    [
        "common.loading", "common.read_only", "common.office_document_preview",
        "chat.this_file_could_not_be", "chat.this_workbook_has_no_visible",
        "chat.read_only_preview_macros_scripts", "chat.zoom_out", "chat.zoom_in",
        "chat.fit_width", "chat.sheets", "chat.showing_the_first_rows_rows",
    ];

    /// <summary>Initializes a new instance of the <see cref="OfficeWebView"/> class.</summary>
    /// <param name="path">Canonical file path.</param><param name="bytes">Authorized file bytes.</param>
    /// <param name="language">The display language.</param><param name="failure">Displays the preview error.</param>
    internal OfficeWebView(string path, byte[] bytes, InterfaceLanguage language, Action failure)
    {
        var guest = WorkbenchWebGuest.Create();
        var requested = false;
        var supplied = false;
        guest.NewWindowRequested += (_, e) => e.Handled = true;
        guest.NavigationStarted += (_, e) => e.Cancel = e.Request?.AbsoluteUri is not (null or "about:blank" or "about:srcdoc");
        guest.AdapterCreated += (_, _) =>
        {
            try
            {
                supplied = false;
                requested = true;
                guest.NavigateToString(s_document.Value, new Uri("about:blank"));
            }
            catch (Exception)
            {
                failure();
            }
        };
        guest.NavigationCompleted += async (_, e) =>
        {
            if (!requested || supplied)
            {
                return;
            }

            if (!e.IsSuccess)
            {
                failure();
                return;
            }

            try
            {
                var ready = await guest.InvokeScript("document.getElementById('office') ? typeof reasonixOffice : 'pending'").ConfigureAwait(true);
                if (ready?.Trim('"') == "pending")
                {
                    return;
                }

                if (ready?.Trim('"') != "object")
                {
                    failure();
                    return;
                }

                supplied = true;
                var messages = s_labels.ToDictionary(key => key, key => MessageCatalog.Get(key, language), StringComparer.Ordinal);
                const int PacketSize = 72 * 1024;
                for (var offset = 0; offset < bytes.Length; offset += PacketSize)
                {
                    var packet = Convert.ToBase64String(bytes, offset, Math.Min(PacketSize, bytes.Length - offset));
                    await guest.InvokeScript("reasonixOffice.append('" + packet + "')").ConfigureAwait(true);
                }

                var arguments = string.Join(",", JsonSerializer.Serialize(path), JsonSerializer.Serialize(messages), JsonSerializer.Serialize(Colors()));
                await guest.InvokeScript("reasonixOffice.show(" + arguments + ")").ConfigureAwait(true);
            }
            catch (Exception)
            {
                failure();
            }
        };
        ActualThemeVariantChanged += async (_, _) =>
        {
            if (supplied && guest.AdapterInfo is not null)
            {
                try
                {
                    await guest.InvokeScript("reasonixOffice.theme(" + JsonSerializer.Serialize(Colors()) + ")").ConfigureAwait(true);
                }
                catch (InvalidOperationException)
                {
                }
            }
        };
        guest.ProcessTerminated += (_, _) => failure();
        Child = guest;
    }

    /// <summary>Reads the isolated renderer from the Avalonia assembly.</summary>
    /// <returns>The self-contained HTML, CSS and JavaScript.</returns>
    private static string ReadDocument()
    {
        using var asset = AssetLoader.Open(new Uri("avares://Reasonix.Studio.Shell/Assets/office-preview.html.gz"));
        using var compressed = new GZipStream(asset, CompressionMode.Decompress);
        using var reader = new StreamReader(compressed);
        return reader.ReadToEnd();
    }

    /// <summary>Resolves the window's active theme into the shared preview's CSS tokens.</summary>
    /// <returns>CSS variables for the current theme.</returns>
    private Dictionary<string, string> Colors()
    {
        var names = new Dictionary<string, string>
        {
            ["--page"] = "PageBrush",
            ["--surface"] = "SurfaceBrush",
            ["--overlay"] = "OverlayBrush",
            ["--raised"] = "RaisedBrush",
            ["--hair"] = "HairBrush",
            ["--border"] = "BorderBrush",
            ["--text"] = "TextBrush",
            ["--muted"] = "MutedBrush",
            ["--faint"] = "FaintBrush",
            ["--accent"] = "AccentBrush",
        };
        var colors = new Dictionary<string, string>(StringComparer.Ordinal);
        var window = TopLevel.GetTopLevel(this);
        foreach (var (name, resource) in names)
        {
            if (((window?.TryFindResource(resource, out var value) ?? false)
                || (Avalonia.Application.Current?.TryGetResource(resource, ActualThemeVariant, out value) ?? false))
                && value is ISolidColorBrush brush)
            {
                var color = brush.Color;
                colors.Add(name, $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}");
            }
        }

        return colors;
    }
}
