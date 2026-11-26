using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Reasonix.Studio.Shell.Browser;
using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Workbench;

/// <summary>Hosts the bundled PDF.js viewer in a disposable, isolated guest.</summary>
internal sealed class PdfWebView : Border
{
    /// <summary>The complete offline PDF document.</summary>
    private static readonly Lazy<(string Html, byte[] Script)> s_document = new(ReadDocument);

    /// <summary>The system rendering adapter.</summary>
    private readonly NativeWebView _guest = WorkbenchWebGuest.Create();

    /// <summary>Ends pending browser calls when the preview is replaced.</summary>
    private readonly CancellationTokenSource _closed = new();

    /// <summary>Notifies the native toolbar of the painted scale.</summary>
    private readonly Action<int> _drawn;

    /// <summary>Shows a failed document or render.</summary>
    private readonly Action _failed;

    /// <summary>Tracks the only render still allowed to update the toolbar.</summary>
    private CancellationTokenSource? _render;

    /// <summary>Whether the guest has accepted the embedded viewer script.</summary>
    private bool _loaded;

    /// <summary>Initializes a new instance of the <see cref="PdfWebView"/> class.</summary>
    /// <param name="bytes">Version-checked bytes from the workspace host.</param>
    /// <param name="opened">Reports the parsed page count.</param><param name="drawn">Reports the painted zoom percentage.</param>
    /// <param name="failed">Reports a failed document or render.</param>
    internal PdfWebView(byte[] bytes, Action<int> opened, Action<int> drawn, Action failed)
    {
        _drawn = drawn;
        _failed = failed;
        var requested = false;
        var started = false;
        _guest.NewWindowRequested += (_, e) => e.Handled = true;
        _guest.NavigationStarted += (_, e) => e.Cancel = e.Request?.AbsoluteUri is not (null or "about:blank");
        _guest.AdapterCreated += (_, _) =>
        {
            try
            {
                requested = true;
                _guest.NavigateToString(s_document.Value.Html, new Uri("about:blank"));
            }
            catch (Exception)
            {
                failed();
            }
        };
        _guest.NavigationCompleted += async (_, e) =>
        {
            if (!requested || started || _closed.IsCancellationRequested)
            {
                return;
            }

            if (!e.IsSuccess)
            {
                failed();
                return;
            }

            try
            {
                var ready = await _guest.InvokeScript("typeof reasonixPdfBoot").ConfigureAwait(true);
                if (ready?.Trim('"') != "object" || started || _closed.IsCancellationRequested)
                {
                    return;
                }

                started = true;
                using var load = CancellationTokenSource.CreateLinkedTokenSource(_closed.Token);
                load.CancelAfter(TimeSpan.FromSeconds(30));
                await SendAsync("reasonixPdfBoot", s_document.Value.Script, load.Token).ConfigureAwait(true);
                await _guest.InvokeScript("reasonixPdfBoot.start()").WaitAsync(load.Token).ConfigureAwait(true);
                if ((await _guest.InvokeScript("typeof reasonixPdf").WaitAsync(load.Token).ConfigureAwait(true))?.Trim('"') != "object")
                {
                    failed();
                    return;
                }

                _loaded = true;
                await UpdateThemeAsync().ConfigureAwait(true);
                await SendAsync("reasonixPdf", bytes, load.Token).ConfigureAwait(true);
                await _guest.InvokeScript("reasonixPdf.open()").WaitAsync(load.Token).ConfigureAwait(true);
                opened(await WaitForNumberAsync("reasonixPdf.pageCount()", load.Token).ConfigureAwait(true));
            }
            catch (OperationCanceledException) when (_closed.IsCancellationRequested)
            {
            }
            catch (Exception)
            {
                failed();
            }
        };
        _guest.ProcessTerminated += (_, _) => failed();
        ActualThemeVariantChanged += async (_, _) => await UpdateThemeAsync().ConfigureAwait(true);
        DetachedFromVisualTree += (_, _) => _closed.Cancel();
        Child = _guest;
    }

    /// <summary>Draws the selected page and rejects a superseded render result.</summary>
    /// <param name="page">One-based page number.</param><param name="zoom">Scale, or zero for fit-width.</param>
    internal async void Draw(int page, double zoom)
    {
        if (_closed.IsCancellationRequested)
        {
            return;
        }

        _render?.Cancel();
        using var render = CancellationTokenSource.CreateLinkedTokenSource(_closed.Token);
        _render = render;
        render.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var command = string.Create(CultureInfo.InvariantCulture, $"reasonixPdf.draw({page},{zoom})");
            await _guest.InvokeScript(command).WaitAsync(render.Token).ConfigureAwait(true);
            var scale = await WaitForNumberAsync("reasonixPdf.drawnScale()", render.Token).ConfigureAwait(true);
            if (_render == render)
            {
                _drawn(scale);
            }
        }
        catch (OperationCanceledException) when (_render != render || _closed.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (_render == render)
            {
                _failed();
            }
        }
        finally
        {
            if (_render == render)
            {
                _render = null;
            }
        }
    }

    /// <summary>Reads the compressed PDF viewer from the application assembly.</summary>
    /// <returns>The viewer shell and offline script.</returns>
    private static (string Html, byte[] Script) ReadDocument()
    {
        using var asset = AssetLoader.Open(new Uri("avares://Reasonix.Studio.Shell/Assets/pdf-preview.html.gz"));
        using var compressed = new GZipStream(asset, CompressionMode.Decompress);
        using var reader = new StreamReader(compressed);
        var html = JsonSerializer.Deserialize<string>(reader.ReadLine() ?? string.Empty) ?? throw new InvalidDataException();
        return (html, Encoding.UTF8.GetBytes(reader.ReadToEnd()));
    }

    /// <summary>Applies the surrounding workbench theme to the guest's canvas background.</summary>
    /// <returns>The optional theme update.</returns>
    private async Task UpdateThemeAsync()
    {
        if (!_loaded || _closed.IsCancellationRequested || TopLevel.GetTopLevel(this) is not { } owner
            || !owner.TryFindResource("PageBrush", out var brush) || brush is not ISolidColorBrush page)
        {
            return;
        }

        var color = page.Color;
        var value = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        try
        {
            await _guest.InvokeScript("reasonixPdf.theme(" + JsonSerializer.Serialize(value) + ")").ConfigureAwait(true);
        }
        catch (InvalidOperationException)
        {
        }
    }

    /// <summary>Passes bundled code or document bytes in bounded chunks to the isolated viewer.</summary>
    /// <param name="receiver">The bundled receiving API.</param><param name="bytes">The bytes to append.</param>
    /// <param name="token">Cancels the transfer.</param><returns>The transfer.</returns>
    private async Task SendAsync(string receiver, byte[] bytes, CancellationToken token)
    {
        const int PacketSize = 72 * 1024;
        for (var offset = 0; offset < bytes.Length; offset += PacketSize)
        {
            var packet = Convert.ToBase64String(bytes, offset, Math.Min(PacketSize, bytes.Length - offset));
            await _guest.InvokeScript(receiver + ".append('" + packet + "')").WaitAsync(token).ConfigureAwait(true);
        }
    }

    /// <summary>Reads the renderer's synchronous result once its JavaScript task has completed.</summary>
    /// <param name="script">The bundled renderer's numeric status function.</param>
    /// <param name="token">Cancels a stale or timed-out render.</param><returns>The positive result.</returns>
    private async Task<int> WaitForNumberAsync(string script, CancellationToken token)
    {
        while (true)
        {
            var answer = await _guest.InvokeScript(script).WaitAsync(token).ConfigureAwait(true);
            if (int.TryParse(answer?.Trim('"'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value != 0)
            {
                return value > 0 ? value : throw new InvalidDataException();
            }

            await Task.Delay(30, token).ConfigureAwait(true);
        }
    }
}
