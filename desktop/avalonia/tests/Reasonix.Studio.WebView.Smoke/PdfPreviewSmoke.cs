using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Reasonix.Studio.Kernel.Client;
using Reasonix.Studio.Pages.I18n;
using Reasonix.Studio.Shell.Workbench;
using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.WebView.Smoke;

/// <summary>Verifies the embedded PDF renderer in the system WebView.</summary>
internal static class PdfPreviewSmoke
{
    /// <summary>Checks selectable PDF text and the native page and zoom controls.</summary>
    /// <returns>The platform observation.</returns>
    internal static async Task RunAsync()
    {
        var buffer = new FileBuffer(new WorkspaceFile("report.pdf", string.Empty, "v1", ReadOnly: true), "pdf");
        var preview = new PdfDocumentPreview(buffer, InterfaceLanguage.En, () => Task.CompletedTask, open: null);
        var window = new Window { Width = 720, Height = 520, Title = "PDF preview smoke", Content = preview };
        window.Show();
        try
        {
            preview.Show(Fixture());
            window.UpdateLayout();
            var guest = preview.GetVisualDescendants().OfType<NativeWebView>().Single();
            await UntilAsync(guest, "document.querySelector('.textLayer')?.textContent.includes('Reasonix first page')").ConfigureAwait(true);
            preview.FindControl<Button>("Next")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await UntilAsync(guest, "document.querySelector('.textLayer')?.textContent.includes('Reasonix second page')").ConfigureAwait(true);
            preview.FindControl<Button>("ZoomIn")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await UntilAsync(guest, "reasonixPdf.drawnScale() === 200").ConfigureAwait(true);
            if (buffer.PdfPosition != (2, 2))
            {
                throw new InvalidOperationException("The native PDF toolbar did not retain the selected page and zoom.");
            }

            Console.WriteLine("PDF preview ready: two pages, selectable text and native zoom");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Waits for the native adapter to produce the expected viewer state.</summary>
    /// <param name="guest">The mounted preview adapter.</param><param name="condition">The DOM observation.</param>
    /// <returns>The settled browser state.</returns>
    private static async Task UntilAsync(NativeWebView guest, string condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        while (!timeout.IsCancellationRequested)
        {
            if (guest.AdapterInfo is not null)
            {
                try
                {
                    if ((await guest.InvokeScript(condition).WaitAsync(timeout.Token).ConfigureAwait(true))?.Trim('"') == "true")
                    {
                        return;
                    }
                }
                catch (InvalidOperationException)
                {
                }
            }

            await Task.Delay(50, timeout.Token).ConfigureAwait(true);
        }

        throw new TimeoutException("The PDF guest did not render the expected page.");
    }

    /// <summary>Creates a small two-page PDF with embedded text operations.</summary>
    /// <returns>The file bytes.</returns>
    private static byte[] Fixture()
    {
        const string Resources = "/MediaBox [0 0 400 300] /Resources << /Font << /F1 5 0 R >> >>";
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R " + Resources + " /Contents 6 0 R >>",
            "<< /Type /Page /Parent 2 0 R " + Resources + " /Contents 7 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            Stream("Reasonix first page"),
            Stream("Reasonix second page"),
        ];
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new int[objects.Length];
        for (var index = 0; index < objects.Length; index++)
        {
            offsets[index] = pdf.Length;
            pdf.Append(CultureInfo.InvariantCulture, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        var xref = pdf.Length;
        pdf.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            pdf.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        pdf.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());

        static string Stream(string text)
        {
            var body = "BT /F1 20 Tf 25 245 Td (" + text + ") Tj ET";
            return string.Create(CultureInfo.InvariantCulture, $"<< /Length {body.Length} >>\nstream\n{body}\nendstream");
        }
    }
}
