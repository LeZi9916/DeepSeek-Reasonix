using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Reasonix.Studio.Pages.I18n;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Reasonix.Studio.Shell.Workbench;

/// <summary>Provides native document navigation around the isolated PDF renderer.</summary>
internal sealed partial class PdfDocumentPreview : UserControl
{
    /// <summary>The zoom steps offered by the reference preview.</summary>
    private static readonly double[] s_steps = [0.5, 0.75, 1, 1.25, 1.5, 2, 3];

    /// <summary>The existing state of this version of the file.</summary>
    private readonly FileBuffer _buffer;

    /// <summary>Language for the preview's labels and errors.</summary>
    private readonly InterfaceLanguage _language;

    /// <summary>Coalesces width changes while fitting the page.</summary>
    private readonly DispatcherTimer _resize = new() { Interval = TimeSpan.FromMilliseconds(100) };

    /// <summary>Number of pages in the acknowledged document.</summary>
    private int _pages;

    /// <summary>The last painted zoom as a fractional scale.</summary>
    private double _drawn = 1;

    /// <summary>Initializes a new instance of the <see cref="PdfDocumentPreview"/> class.</summary>
    /// <param name="buffer">The selected file and its retained navigation.</param>
    /// <param name="language">The interface language.</param><param name="refresh">Re-reads the selected file.</param>
    /// <param name="open">Opens the file in a system application, when supported.</param>
    internal PdfDocumentPreview(FileBuffer buffer, InterfaceLanguage language, Func<Task> refresh, Func<Task>? open)
    {
        InitializeComponent();
        _buffer = buffer;
        _language = language;
        Label(Previous, "common.previous_page");
        Label(Next, "common.next_page");
        Label(ZoomOut, "chat.zoom_out");
        Label(ZoomIn, "chat.zoom_in");
        Label(PageNumber, "chat.page_number");
        Fit.Content = Text("chat.fit_width");
        Refresh.Content = Text("common.re_freeze");
        OpenInApp.Content = Text("chat.open_in_system_application");
        Notice.Text = Text("common.loading");
        OpenInApp.IsVisible = open is not null;
        if (open is not null)
        {
            OpenInApp.Click += async (_, _) => await open().ConfigureAwait(true);
        }

        Refresh.Click += async (_, _) => await refresh().ConfigureAwait(true);
        PageNumber.ValueChanged += (_, e) =>
        {
            if (e.NewValue is { } value && _pages > 0)
            {
                Go((int)value);
            }
        };
        Previous.Click += (_, _) => Go(_buffer.PdfPosition.Page - 1);
        Next.Click += (_, _) => Go(_buffer.PdfPosition.Page + 1);
        ZoomOut.Click += (_, _) => SetZoom(s_steps.LastOrDefault(step => step < _drawn - 0.01, s_steps[0]));
        ZoomIn.Click += (_, _) => SetZoom(s_steps.FirstOrDefault(step => step > _drawn + 0.01, s_steps[^1]));
        Fit.Click += (_, _) => SetZoom(0);
        Document.SizeChanged += (_, _) =>
        {
            if (_buffer.PdfPosition.Zoom == 0 && _pages > 0)
            {
                _resize.Stop();
                _resize.Start();
            }
        };
        _resize.Tick += (_, _) =>
        {
            _resize.Stop();
            Redraw();
        };
        DetachedFromVisualTree += (_, _) => _resize.Stop();
        RefreshCommands();
    }

    /// <summary>Gets the canonical path shown here.</summary>
    internal string Path => _buffer.Original.Path;

    /// <summary>Opens version-checked bytes in the PDF-only renderer.</summary>
    /// <param name="bytes">The document bytes supplied by the workspace host.</param>
    internal void Show(byte[] bytes)
    {
        Document.Content = new PdfWebView(
            bytes,
            pages =>
            {
                _pages = pages;
                Total.Text = "/ " + pages.ToString(CultureInfo.CurrentCulture);
                PageNumber.Maximum = pages;
                Go(_buffer.PdfPosition.Page);
                Notice.IsVisible = false;
            },
            scale =>
            {
                _drawn = scale / 100d;
                Zoom.Text = scale.ToString(CultureInfo.CurrentCulture) + "%";
            },
            () => ShowFailure());
    }

    /// <summary>Displays an unavailable document or an oversized file in the preview.</summary>
    /// <param name="message">A localized refusal, when the host supplied a specific cause.</param>
    internal void ShowFailure(string? message = null)
    {
        _pages = 0;
        _resize.Stop();
        Document.Content = null;
        Notice.Text = message ?? Text("chat.cannot_display_this_pdf_it");
        Notice.IsVisible = true;
        RefreshCommands();
    }

    /// <summary>Moves to a valid one-based page and retains it across tab switches.</summary>
    /// <param name="page">The page requested by the reader.</param>
    private void Go(int page)
    {
        if (_pages == 0)
        {
            return;
        }

        var target = Math.Clamp(page, 1, _pages);
        _buffer.PdfPosition = (target, _buffer.PdfPosition.Zoom);
        if (PageNumber.Value != target)
        {
            PageNumber.Value = target;
            return;
        }

        RefreshCommands();
        Redraw();
    }

    /// <summary>Applies the requested zoom or fit-width mode.</summary>
    /// <param name="zoom">The scale, or zero for fit width.</param>
    private void SetZoom(double zoom)
    {
        _buffer.PdfPosition = (_buffer.PdfPosition.Page, zoom);
        Redraw();
    }

    /// <summary>Updates the renderer from this file's current navigation.</summary>
    private void Redraw()
    {
        if (_pages > 0 && Document.Content is PdfWebView guest)
        {
            guest.Draw(_buffer.PdfPosition.Page, _buffer.PdfPosition.Zoom);
        }
    }

    /// <summary>Enables only commands that can move the current document.</summary>
    private void RefreshCommands()
    {
        var loaded = _pages > 0;
        Previous.IsEnabled = loaded && _buffer.PdfPosition.Page > 1;
        Next.IsEnabled = loaded && _buffer.PdfPosition.Page < _pages;
        PageNumber.IsEnabled = Fit.IsEnabled = ZoomIn.IsEnabled = ZoomOut.IsEnabled = loaded;
    }

    /// <summary>Applies localized accessible names to icon-only controls.</summary>
    /// <param name="control">The action's control.</param><param name="key">The message key.</param>
    private void Label(Control control, string key)
    {
        var label = Text(key);
        AutomationProperties.SetName(control, label);
        ToolTip.SetTip(control, label);
    }

    /// <summary>Looks up a localized label.</summary>
    /// <param name="key">The message identity.</param><returns>The label in the active language.</returns>
    private string Text(string key) => MessageCatalog.Get(key, _language);
}
