using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Workbench;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.WebView.Smoke;

/// <summary>Exercises the same isolated document renderer that the workbench mounts.</summary>
internal static class OfficePreviewSmoke
{
    /// <summary>Loads a real Word document and validates its sandbox and zoom controls.</summary>
    /// <returns>The desktop adapter observation.</returns>
    internal static async Task RunAsync()
    {
        var samples = Environment.GetEnvironmentVariable("STUDIO_OFFICE_SAMPLES");
        var dark = Environment.GetEnvironmentVariable("STUDIO_OFFICE_DARK") == "1";
        var compact = Environment.GetEnvironmentVariable("STUDIO_OFFICE_COMPACT") == "1";
        var window = new Window
        {
            Title = "Reasonix Office preview",
            Width = 508,
            Height = compact ? 332 : 752,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
        };
        window.Show();
        try
        {
            var word = samples is null ? WordFixture() : File.ReadAllBytes(Path.Combine(samples, "report.docx"));
            var wordView = await OpenAsync("report.docx", word, samples is null ? "Reasonix quarterly report" : "季度报告").ConfigureAwait(true);
            Console.WriteLine("Office preview ready: document");
            await HoldAsync().ConfigureAwait(true);
            await wordView.InvokeScript("document.querySelector('[data-action=\"document.zoom\"][aria-label=\"放大\"]').click()").ConfigureAwait(true);
            await UntilAsync(wordView, "document.querySelector('.pdf-fit')?.getAttribute('aria-pressed') === 'false'").ConfigureAwait(true);
            Console.WriteLine($"{wordView.AdapterInfo?.Type}: Word rendering, sandbox and zoom passed.");

            if (samples is not null)
            {
                var sheets = await OpenAsync("sales.xlsx", File.ReadAllBytes(Path.Combine(samples, "sales.xlsx")), "1100").ConfigureAwait(true);
                Console.WriteLine("Office preview ready: workbook");
                await HoldAsync().ConfigureAwait(true);
                await sheets.InvokeScript("document.querySelector('.office-sheets [role=tab]:last-child').click()").ConfigureAwait(true);
                await UntilAsync(sheets, "document.querySelector('iframe')?.srcdoc.includes('12480')").ConfigureAwait(true);
                Console.WriteLine($"{sheets.AdapterInfo?.Type}: XLSX rendering and switching worksheets passed.");
                Console.WriteLine("Office preview ready: workbook-details");
                await HoldAsync().ConfigureAwait(true);
            }
        }
        finally
        {
            window.Close();
        }

        async Task<NativeWebView> OpenAsync(string path, byte[] contents, string expected)
        {
            var preview = new OfficeDocumentPreview(path, contents.Length, 32 << 20, true, InterfaceLanguage.Zh, () => Task.CompletedTask, () => Task.CompletedTask, () => Task.CompletedTask);
            window.Content = preview;
            preview.Show(contents);
            window.UpdateLayout();
            var guest = preview.GetVisualDescendants().OfType<NativeWebView>().Single();
            var value = System.Text.Json.JsonSerializer.Serialize(expected);
            await UntilAsync(
                guest,
                $"document.querySelector('iframe')?.srcdoc.includes({value}) && document.querySelector('iframe').getAttribute('sandbox') === '' && document.querySelector('iframe').srcdoc.includes('script-src')").ConfigureAwait(true);
            await UntilAsync(guest, "document.documentElement.style.getPropertyValue('--page').length > 0").ConfigureAwait(true);
            return guest;
        }
    }

    /// <summary>Waits for the actual WebView to render a checked document state.</summary>
    /// <param name="guest">Native browser adapter.</param><param name="condition">Boolean DOM expression.</param>
    /// <returns>The browser observation.</returns>
    private static async Task UntilAsync(NativeWebView guest, string condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!timeout.IsCancellationRequested)
        {
            if (guest.AdapterInfo is not null)
            {
                try
                {
                    var result = await guest.InvokeScript(condition).WaitAsync(timeout.Token).ConfigureAwait(true);
                    if (result?.Trim('"') == "true")
                    {
                        return;
                    }
                }
                catch (InvalidOperationException) { }
            }

            await Task.Delay(50, timeout.Token).ConfigureAwait(true);
        }

        throw new TimeoutException("The Office guest did not render the expected local state.");
    }

    /// <summary>Leaves an optional capture interval when running against local visual fixtures.</summary>
    /// <returns>The capture interval.</returns>
    private static Task HoldAsync() => int.TryParse(Environment.GetEnvironmentVariable("STUDIO_OFFICE_SHOT_MS"), out var delay) && delay > 0
        ? Task.Delay(delay)
        : Task.CompletedTask;

    /// <summary>Builds a small standards-based Word document for the cross-platform smoke.</summary>
    /// <returns>A document containing one formatted paragraph.</returns>
    private static byte[] WordFixture()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Part("[Content_Types].xml", """<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>""");
            Part("_rels/.rels", """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>""");
            Part("word/document.xml", """<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:rPr><w:b/></w:rPr><w:t>Reasonix quarterly report</w:t></w:r></w:p><w:sectPr><w:pgSz w:w="11906" w:h="16838"/></w:sectPr></w:body></w:document>""");

            void Part(string path, string xml)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open());
                writer.Write(xml);
            }
        }

        return buffer.ToArray();
    }
}
